using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EnterpriseInventory.Application.Abstractions;
using EnterpriseInventory.Application.Authentication;
using EnterpriseInventory.Domain.Auditing;
using EnterpriseInventory.Domain.Users;
using EnterpriseInventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnterpriseInventory.Infrastructure.Identity;

internal sealed partial class UserSessionService(
    ApplicationDbContext db,
    SignInIdentity signInIdentity,
    IDirectoryService directory,
    IRequestContext requestContext,
    IOptions<UserSessionOptions> options,
    TimeProvider timeProvider,
    ILogger<UserSessionService> logger) : IUserSessionService
{
    /// <summary>Names the system as the actor of changes no user asked for, such as revoking access.</summary>
    public const string SystemUserName = "system";

    private const int KeyBytes = 32;

    public async Task<StartedSession> StartAsync(DirectoryAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        using var actingUser = signInIdentity.ActAs(account.SamAccountName);
        try
        {
            return await StartCoreAsync(account, cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueKeyViolation())
        {
            // The first two sign-ins of a new administrator can race to create the record; the loser finds the
            // winner's record on a second attempt.
            db.ChangeTracker.Clear();
            return await StartCoreAsync(account, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<StartedSession> StartCoreAsync(DirectoryAccount account, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var key = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(KeyBytes));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var user = await db.AdminUsers.SingleOrDefaultAsync(u => u.ObjectGuid == account.ObjectGuid, cancellationToken)
            .ConfigureAwait(false);
        if (user is null)
        {
            user = AdminUser.Create(account.ObjectGuid, account.SamAccountName, account.DisplayName, now);
            db.AdminUsers.Add(user);
        }
        else
        {
            user.RecordLogin(account.SamAccountName, account.DisplayName, now);
        }

        var session = UserSession.Start(user, Hash(key), now, options.Value.Timeouts, requestContext.ClientAddress);
        db.UserSessions.Add(session);

        // Saved first so the audit entry can name the records by their IDs.
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Audit(user, AuditAction.SignedIn, user.SamAccountName, now, new
        {
            user.SamAccountName,
            user.DisplayName,
            SessionId = session.Id,
            session.ClientAddress,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new StartedSession(key, session.ExpiresAt);
    }

    public Task<SessionValidationResult> ValidateAsync(string sessionKey, CancellationToken cancellationToken) =>
        EvaluateAsync(sessionKey, recordActivity: true, cancellationToken);

    public Task<SessionValidationResult> CheckAsync(string sessionKey, CancellationToken cancellationToken) =>
        EvaluateAsync(sessionKey, recordActivity: false, cancellationToken);

    private async Task<SessionValidationResult> EvaluateAsync(string sessionKey, bool recordActivity, CancellationToken cancellationToken)
    {
        var session = await FindAsync(sessionKey, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return SessionValidationResult.Invalid(null);
        }

        var now = timeProvider.GetUtcNow();
        var timeouts = options.Value.Timeouts;
        using var actingUser = signInIdentity.ActAs(session.AdminUser.SamAccountName);

        switch (session.Evaluate(now, timeouts))
        {
            case SessionState.Ended:
                return SessionValidationResult.Invalid(session.EndReason);

            case SessionState.AbsoluteTimeoutReached:
                return await EndAsync(session, SessionEndReason.AbsoluteTimeout, now, cancellationToken).ConfigureAwait(false);

            case SessionState.IdleTimeoutReached:
                return await EndAsync(session, SessionEndReason.IdleTimeout, now, cancellationToken).ConfigureAwait(false);

            case SessionState.AccessCheckDue:
                var access = await directory.CheckAccessAsync(session.AdminUser.ObjectGuid, cancellationToken).ConfigureAwait(false);
                if (access == DirectoryAccessStatus.Allowed)
                {
                    session.RecordAccessConfirmed(now);
                }
                else if (access == DirectoryAccessStatus.DirectoryUnavailable)
                {
                    if (session.RecordAccessCheckFailed(now, timeouts))
                    {
                        return await EndAsync(session, SessionEndReason.DirectoryUnavailable, now, cancellationToken).ConfigureAwait(false);
                    }

                    LogAccessCheckDeferred(session.AdminUser.SamAccountName, session.LastAccessCheckAt);
                }
                else
                {
                    return await RevokeAsync(session, access, now, cancellationToken).ConfigureAwait(false);
                }

                break;
        }

        if (recordActivity)
        {
            session.RecordActivity(now);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return SessionValidationResult.Valid;
    }

    public async Task EndAsync(string sessionKey, CancellationToken cancellationToken)
    {
        var session = await FindAsync(sessionKey, cancellationToken).ConfigureAwait(false);
        if (session is not { IsActive: true })
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        using var actingUser = signInIdentity.ActAs(session.AdminUser.SamAccountName);
        session.End(SessionEndReason.SignedOut, now);
        Audit(session.AdminUser, AuditAction.SignedOut, session.AdminUser.SamAccountName, now, new { SessionId = session.Id });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        LogSessionEnded(session.AdminUser.SamAccountName, SessionEndReason.SignedOut);
    }

    internal static byte[] Hash(string sessionKey) => SHA256.HashData(Encoding.UTF8.GetBytes(sessionKey));

    private async Task<UserSession?> FindAsync(string sessionKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(sessionKey))
        {
            return null;
        }

        var hash = Hash(sessionKey);
        return await db.UserSessions
            .Include(s => s.AdminUser)
            .SingleOrDefaultAsync(s => s.KeyHash == hash, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<SessionValidationResult> EndAsync(
        UserSession session, SessionEndReason reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        session.End(reason, now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        LogSessionEnded(session.AdminUser.SamAccountName, reason);
        return SessionValidationResult.Invalid(reason);
    }

    private async Task<SessionValidationResult> RevokeAsync(
        UserSession session, DirectoryAccessStatus access, DateTimeOffset now, CancellationToken cancellationToken)
    {
        session.End(SessionEndReason.AccessRevoked, now);
        Audit(session.AdminUser, AuditAction.AccessRevoked, SystemUserName, now, new
        {
            session.AdminUser.SamAccountName,
            SessionId = session.Id,
            Reason = access.ToString(),
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        LogAccessRevoked(session.AdminUser.SamAccountName, access);
        return SessionValidationResult.Invalid(SessionEndReason.AccessRevoked);
    }

    private void Audit(AdminUser user, AuditAction action, string actor, DateTimeOffset now, object details) =>
        db.AuditLogs.Add(AuditLog.Create(
            nameof(AdminUser),
            user.Id.ToString(CultureInfo.InvariantCulture),
            action,
            oldValues: null,
            newValues: JsonSerializer.Serialize(details),
            actor,
            now,
            requestContext.CorrelationId));

    [LoggerMessage(Level = LogLevel.Information, Message = "Session of {UserName} ended: {Reason}")]
    private partial void LogSessionEnded(string userName, SessionEndReason reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Access of {UserName} was revoked by the directory check: {Status}")]
    private partial void LogAccessRevoked(string userName, DirectoryAccessStatus status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The directory could not confirm the access of {UserName}; last confirmed at {LastConfirmedAt}. The session continues within the outage grace period")]
    private partial void LogAccessCheckDeferred(string userName, DateTimeOffset lastConfirmedAt);
}
