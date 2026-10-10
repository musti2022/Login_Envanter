using System.Text.RegularExpressions;
using EnterpriseInventory.Application.Authentication;

namespace EnterpriseInventory.Infrastructure.ActiveDirectory;

/// <summary>
/// Reads why Active Directory refused a bind. AD answers every refused bind with result 49 (invalid credentials)
/// and names the reason in the diagnostic message, e.g.
/// <c>80090308: LdapErr: DSID-0C09044E, comment: AcceptSecurityContext error, data 533, v4563</c>.
/// AD reports an account's state only when the password was right (except lockout), so these reasons do not
/// reveal anything to someone guessing passwords.
/// </summary>
internal static partial class BindFailures
{
    public static DirectorySignInStatus FromDiagnosticMessage(string? message)
    {
        var match = message is null ? null : DataCode().Match(message);
        return match is { Success: true } ? FromCode(match.Groups["code"].Value) : DirectorySignInStatus.InvalidCredentials;
    }

    private static DirectorySignInStatus FromCode(string code) => code.ToLowerInvariant() switch
    {
        "525" or "52e" => DirectorySignInStatus.InvalidCredentials,  // no such user / wrong password
        "530" or "531" => DirectorySignInStatus.LogonNotPermitted,   // logon hours / workstation restriction
        "532" => DirectorySignInStatus.PasswordExpired,
        "533" => DirectorySignInStatus.AccountDisabled,
        "701" => DirectorySignInStatus.AccountExpired,
        "773" => DirectorySignInStatus.PasswordMustChange,
        "775" => DirectorySignInStatus.AccountLocked,
        _ => DirectorySignInStatus.InvalidCredentials,
    };

    [GeneratedRegex(@"\bdata (?<code>[0-9a-fA-F]{3,8})\b")]
    private static partial Regex DataCode();
}
