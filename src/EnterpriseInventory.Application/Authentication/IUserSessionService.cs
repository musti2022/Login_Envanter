using EnterpriseInventory.Domain.Users;

namespace EnterpriseInventory.Application.Authentication;

/// <summary>
/// Server-side sessions of signed-in administrators. The browser only holds the session key (inside the
/// encrypted, HttpOnly cookie); whether the session is still valid is decided here on every request.
/// </summary>
public interface IUserSessionService
{
    /// <summary>
    /// Creates or refreshes the administrator's record, starts a session and appends a sign-in audit entry, all in one
    /// transaction. Admin users are kept apart from employees.
    /// </summary>
    Task<StartedSession> StartAsync(DirectoryAccount account, CancellationToken cancellationToken);

    /// <summary>
    /// Checks the session for the current request: ends it after the idle or absolute timeout, re-checks the
    /// user's access in the directory when it is due and ends the session if access was lost.
    /// </summary>
    Task<SessionValidationResult> ValidateAsync(string sessionKey, CancellationToken cancellationToken);

    /// <summary>Ends the session at the user's request and audits the sign-out.</summary>
    Task EndAsync(string sessionKey, CancellationToken cancellationToken);
}

/// <param name="Key">The secret that identifies the session; it goes only into the session cookie.</param>
public sealed record StartedSession(string Key, DateTimeOffset ExpiresAt);

public sealed record SessionValidationResult(bool IsValid, SessionEndReason? EndReason = null)
{
    public static readonly SessionValidationResult Valid = new(true);

    public static SessionValidationResult Invalid(SessionEndReason? reason) => new(false, reason);
}
