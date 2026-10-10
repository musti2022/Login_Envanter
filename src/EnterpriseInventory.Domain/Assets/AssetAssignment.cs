using EnterpriseInventory.Domain.Common;
using EnterpriseInventory.Domain.Employees;

namespace EnterpriseInventory.Domain.Assets;

/// <summary>
/// One period during which an asset was held by an employee. Created and closed only through
/// <see cref="Asset"/>; records are never deleted so the return history is preserved.
/// </summary>
public sealed class AssetAssignment : Entity
{
    public const int AssignmentDescriptionMaxLength = 500;
    public const int NotesMaxLength = 1000;

    private AssetAssignment()
    {
    }

    internal AssetAssignment(
        Asset asset,
        Employee employee,
        string? assignmentDescription,
        string? notes,
        string assignedBy,
        DateTimeOffset assignedAt)
    {
        Asset = asset;
        AssetId = asset.Id;
        Employee = employee;
        EmployeeId = employee.Id;
        AssignmentDescription = Guard.Optional(assignmentDescription, AssignmentDescriptionMaxLength, nameof(assignmentDescription));
        Notes = Guard.Optional(notes, NotesMaxLength, nameof(notes));
        AssignedBy = Guard.Required(assignedBy, AuditableEntity.UserNameMaxLength, nameof(assignedBy));
        AssignedAt = assignedAt;
    }

    public int AssetId { get; private set; }

    public Asset Asset { get; private set; } = null!;

    public int EmployeeId { get; private set; }

    public Employee Employee { get; private set; } = null!;

    /// <summary>"Zimmet Tanımı": what was handed over, e.g. "Dizüstü bilgisayar + şarj adaptörü".</summary>
    public string? AssignmentDescription { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset AssignedAt { get; private set; }

    public string AssignedBy { get; private set; } = string.Empty;

    public DateTimeOffset? ReturnedAt { get; private set; }

    public string? ReturnedBy { get; private set; }

    public bool IsActive => ReturnedAt is null;

    internal void Close(string returnedBy, DateTimeOffset returnedAt)
    {
        var user = Guard.Required(returnedBy, AuditableEntity.UserNameMaxLength, nameof(returnedBy));
        if (returnedAt < AssignedAt)
        {
            throw new DomainException(DomainErrors.Asset.ReturnBeforeAssignment, "The return time cannot be earlier than the assignment time.");
        }

        ReturnedBy = user;
        ReturnedAt = returnedAt;
    }
}
