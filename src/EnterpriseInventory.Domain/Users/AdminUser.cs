using EnterpriseInventory.Domain.Common;

namespace EnterpriseInventory.Domain.Users;

/// <summary>
/// A person who signed in to the application. Access is decided by Active Directory membership of the
/// Bim_Envanter group at sign-in and on periodic re-checks, never by this record; it only maps the
/// directory identity to the application for auditing. No password or credential is ever stored.
/// </summary>
public sealed class AdminUser : Entity
{
    public const int SamAccountNameMaxLength = 20;
    public const int DisplayNameMaxLength = 256;

    private AdminUser()
    {
    }

    /// <summary>AD objectGUID; stable across renames.</summary>
    public Guid ObjectGuid { get; private set; }

    public string SamAccountName { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public DateTimeOffset FirstLoginAt { get; private set; }

    public DateTimeOffset LastLoginAt { get; private set; }

    public static AdminUser Create(Guid objectGuid, string samAccountName, string displayName, DateTimeOffset loginAt)
    {
        var user = new AdminUser
        {
            ObjectGuid = Guard.NotEmpty(objectGuid, nameof(objectGuid)),
            FirstLoginAt = loginAt,
        };
        user.RecordLogin(samAccountName, displayName, loginAt);
        return user;
    }

    /// <summary>Refreshes the directory names and the last sign-in time after a successful login.</summary>
    public void RecordLogin(string samAccountName, string displayName, DateTimeOffset loginAt)
    {
        var sam = Guard.Required(samAccountName, SamAccountNameMaxLength, nameof(samAccountName));
        var name = Guard.Required(displayName, DisplayNameMaxLength, nameof(displayName));

        SamAccountName = sam;
        DisplayName = name;
        LastLoginAt = loginAt;
    }
}
