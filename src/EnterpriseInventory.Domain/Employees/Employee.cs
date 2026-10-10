using EnterpriseInventory.Domain.Common;

namespace EnterpriseInventory.Domain.Employees;

/// <summary>
/// A person from Active Directory who can be given assets. Employees do not need application access
/// and are kept separate from <see cref="Users.AdminUser"/>, the people who sign in.
/// Profile fields are a copy of the directory and are refreshed from it.
/// </summary>
public sealed class Employee : AuditableEntity
{
    public const int SamAccountNameMaxLength = 20;
    public const int DisplayNameMaxLength = 256;
    public const int EmailMaxLength = 256;
    public const int DepartmentMaxLength = 128;
    public const int TitleMaxLength = 128;

    private Employee()
    {
    }

    /// <summary>AD objectGUID; stable across renames, used to match directory records.</summary>
    public Guid ObjectGuid { get; private set; }

    public string SamAccountName { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string? Email { get; private set; }

    /// <summary>Department text as recorded in AD (not the application's department list).</summary>
    public string? Department { get; private set; }

    public string? Title { get; private set; }

    /// <summary>False when the directory account is disabled; inactive employees cannot receive new assignments.</summary>
    public bool IsActive { get; private set; }

    public DateTimeOffset LastSyncedAt { get; private set; }

    public static Employee Create(
        Guid objectGuid,
        string samAccountName,
        string displayName,
        string? email,
        string? department,
        string? title,
        bool isActive,
        DateTimeOffset syncedAt)
    {
        var employee = new Employee { ObjectGuid = Guard.NotEmpty(objectGuid, nameof(objectGuid)) };
        employee.UpdateFromDirectory(samAccountName, displayName, email, department, title, isActive, syncedAt);
        return employee;
    }

    public void UpdateFromDirectory(
        string samAccountName,
        string displayName,
        string? email,
        string? department,
        string? title,
        bool isActive,
        DateTimeOffset syncedAt)
    {
        // Validate everything first so a rejected update leaves the record unchanged.
        var sam = Guard.Required(samAccountName, SamAccountNameMaxLength, nameof(samAccountName));
        var name = Guard.Required(displayName, DisplayNameMaxLength, nameof(displayName));
        var mail = Guard.Optional(email, EmailMaxLength, nameof(email));
        var dept = Guard.Optional(department, DepartmentMaxLength, nameof(department));
        var jobTitle = Guard.Optional(title, TitleMaxLength, nameof(title));

        SamAccountName = sam;
        DisplayName = name;
        Email = mail;
        Department = dept;
        Title = jobTitle;
        IsActive = isActive;
        LastSyncedAt = syncedAt;
    }
}
