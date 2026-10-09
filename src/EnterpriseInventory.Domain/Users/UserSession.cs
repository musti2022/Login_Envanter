using EnterpriseInventory.Domain.Common;

namespace EnterpriseInventory.Domain.Users;

/// <summary>
/// A signed-in browser session of an <see cref="AdminUser"/>. The session lives on the server: the cookie only
/// carries its key, so a session can be ended (sign-out, timeout, lost group membership) at any moment, whatever
/// the cookie says. Only a SHA-256 hash of the key is stored, so the table cannot be used to take over a session.
/// </summary>
public sealed class UserSession : Entity
{
    public const int KeyHashLength = 32;
    public const int ClientAddressMaxLength = 45;

    /// <summary>Activity is written at most this often, so most requests only read the session.</summary>
    public static readonly TimeSpan ActivityResolution = TimeSpan.FromMinutes(1);

    /// <summary>While the directory is unreachable, the access check is retried at most this often.</summary>
    public static readonly TimeSpan AccessCheckRetryInterval = TimeSpan.FromMinutes(1);

    private UserSession()
    {
    }

    public int AdminUserId { get; private set; }

    public AdminUser AdminUser { get; private set; } = null!;

    public byte[] KeyHash { get; private set; } = [];

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    /// <summary>The absolute end of the session, however active it is.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When the directory last confirmed the user may still use the application.</summary>
    public DateTimeOffset LastAccessCheckAt { get; private set; }

    /// <summary>The last access check that could not reach the directory, if any since the last success.</summary>
    public DateTimeOffset? LastFailedAccessCheckAt { get; private set; }

    public string? ClientAddress { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    public SessionEndReason? EndReason { get; private set; }

    public bool IsActive => EndedAt is null;

    /// <summary>Starts a session right after the directory accepted the user's password and group membership.</summary>
    public static UserSession Start(AdminUser user, byte[] keyHash, DateTimeOffset now, SessionTimeouts timeouts, string? clientAddress)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(keyHash);
        if (keyHash.Length != KeyHashLength)
        {
            throw new DomainException(DomainErrors.InvalidValue, $"The session key hash must be {KeyHashLength} bytes.");
        }

        return new UserSession
        {
            AdminUser = user,
            KeyHash = keyHash,
            StartedAt = now,
            LastSeenAt = now,
            ExpiresAt = now + timeouts.Absolute,
            LastAccessCheckAt = now,
            ClientAddress = Guard.Optional(clientAddress, ClientAddressMaxLength, nameof(clientAddress)),
        };
    }

    /// <summary>What has to happen to this session for a request arriving at <paramref name="now"/>.</summary>
    public SessionState Evaluate(DateTimeOffset now, SessionTimeouts timeouts)
    {
        if (!IsActive)
        {
            return SessionState.Ended;
        }

        if (now >= ExpiresAt)
        {
            return SessionState.AbsoluteTimeoutReached;
        }

        if (now - LastSeenAt >= timeouts.Idle)
        {
            return SessionState.IdleTimeoutReached;
        }

        var checkDue = now - LastAccessCheckAt >= timeouts.AccessRecheck
            && (LastFailedAccessCheckAt is not { } failed || now - failed >= AccessCheckRetryInterval);
        return checkDue ? SessionState.AccessCheckDue : SessionState.Active;
    }

    /// <summary>Records a request; returns whether the stored activity changed.</summary>
    public bool RecordActivity(DateTimeOffset now)
    {
        EnsureActive();
        if (now - LastSeenAt < ActivityResolution)
        {
            return false;
        }

        LastSeenAt = now;
        return true;
    }

    public void RecordAccessConfirmed(DateTimeOffset now)
    {
        EnsureActive();
        LastAccessCheckAt = now;
        LastFailedAccessCheckAt = null;
    }

    /// <summary>
    /// Records an access check that could not reach the directory. Returns whether the session has now gone too long
    /// without confirmation and must end.
    /// </summary>
    public bool RecordAccessCheckFailed(DateTimeOffset now, SessionTimeouts timeouts)
    {
        EnsureActive();
        LastFailedAccessCheckAt = now;
        return now - LastAccessCheckAt >= timeouts.AccessRecheck + timeouts.DirectoryOutageGrace;
    }

    /// <summary>Ends the session; a session that already ended keeps its first reason.</summary>
    public void End(SessionEndReason reason, DateTimeOffset now)
    {
        Guard.Defined(reason, nameof(reason));
        if (!IsActive)
        {
            return;
        }

        EndedAt = now;
        EndReason = reason;
    }

    private void EnsureActive()
    {
        if (!IsActive)
        {
            throw new DomainException(DomainErrors.Session.Ended, "The session has ended.");
        }
    }
}

/// <summary>The time limits of a session.</summary>
/// <param name="Idle">The session ends after this long without a request.</param>
/// <param name="Absolute">The session ends this long after sign-in, however active.</param>
/// <param name="AccessRecheck">Group membership and account state are checked in the directory this often.</param>
/// <param name="DirectoryOutageGrace">
/// How long past <paramref name="AccessRecheck"/> a session may continue while the directory cannot be reached.
/// </param>
public readonly record struct SessionTimeouts(TimeSpan Idle, TimeSpan Absolute, TimeSpan AccessRecheck, TimeSpan DirectoryOutageGrace);

public enum SessionState
{
    Active = 0,
    AccessCheckDue = 1,
    IdleTimeoutReached = 2,
    AbsoluteTimeoutReached = 3,
    Ended = 4,
}

/// <summary>Why a session ended. Stored as int; values must not be renumbered.</summary>
public enum SessionEndReason
{
    SignedOut = 1,
    IdleTimeout = 2,
    AbsoluteTimeout = 3,

    /// <summary>The directory no longer allows the user in: not a member, disabled, expired or not found.</summary>
    AccessRevoked = 4,

    /// <summary>The directory could not be reached for longer than the outage grace period.</summary>
    DirectoryUnavailable = 5,

    /// <summary>Ended by an administrator, e.g. directly in the database.</summary>
    Revoked = 6,
}
