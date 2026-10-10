namespace EnterpriseInventory.Application.Authentication;

/// <summary>
/// The organisation's directory (Active Directory). Implementations verify the password with the directory itself,
/// decide membership of the allowed group by its SID and fail closed: any doubt is a refusal. The password is
/// only passed through to the directory over a verified TLS connection; it is never stored or logged.
/// </summary>
public interface IDirectoryService
{
    Task<DirectorySignInResult> SignInAsync(string userName, string password, CancellationToken cancellationToken);

    /// <summary>
    /// Checks, with the service account, that a signed-in user may still use the application: the account still
    /// exists under BaseDn, is enabled and not expired, and is still a member of the allowed group.
    /// </summary>
    /// <param name="objectGuid">The user's AD objectGUID, recorded at sign-in.</param>
    Task<DirectoryAccessStatus> CheckAccessAsync(Guid objectGuid, CancellationToken cancellationToken);
}

public enum DirectoryAccessStatus
{
    Allowed = 0,

    /// <summary>No longer a member of the allowed group.</summary>
    NotAuthorized = 1,
    AccountDisabled = 2,
    AccountExpired = 3,

    /// <summary>The account was deleted or moved outside BaseDn.</summary>
    AccountNotFound = 4,

    /// <summary>The directory could not be reached or trusted, so access could not be confirmed.</summary>
    DirectoryUnavailable = 5,
}

public sealed record DirectorySignInResult(DirectorySignInStatus Status, DirectoryAccount? Account = null)
{
    public static DirectorySignInResult Succeeded(DirectoryAccount account) => new(DirectorySignInStatus.Succeeded, account);

    public static DirectorySignInResult Failed(DirectorySignInStatus status) =>
        status == DirectorySignInStatus.Succeeded
            ? throw new ArgumentException("A successful result needs the account.", nameof(status))
            : new(status);
}

/// <summary>A directory user who proved their password and is a member of the allowed group.</summary>
/// <param name="ObjectGuid">AD objectGUID; stable across renames.</param>
/// <param name="SecurityIdentifier">The user's objectSid, e.g. S-1-5-21-…-1105.</param>
public sealed record DirectoryAccount(Guid ObjectGuid, string SecurityIdentifier, string SamAccountName, string DisplayName);

public enum DirectorySignInStatus
{
    Succeeded = 0,

    /// <summary>Unknown user or wrong password; the directory does not say which.</summary>
    InvalidCredentials = 1,
    AccountDisabled = 2,
    AccountLocked = 3,
    AccountExpired = 4,
    PasswordExpired = 5,
    PasswordMustChange = 6,

    /// <summary>Logon hours or workstation restrictions forbid signing in now.</summary>
    LogonNotPermitted = 7,

    /// <summary>The password is right but the user is not a member of the allowed group (or is outside BaseDn).</summary>
    NotAuthorized = 8,

    /// <summary>The directory could not be reached or trusted; new sign-ins are refused.</summary>
    DirectoryUnavailable = 9,
}
