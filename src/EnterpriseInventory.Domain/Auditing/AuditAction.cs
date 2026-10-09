namespace EnterpriseInventory.Domain.Auditing;

/// <summary>Values are stored in the database; never renumber existing members.</summary>
public enum AuditAction
{
    Created = 1,
    Updated = 2,
    Archived = 3,
    Assigned = 4,
    Returned = 5,
    LocationChanged = 6,
    StatusChanged = 7,
}
