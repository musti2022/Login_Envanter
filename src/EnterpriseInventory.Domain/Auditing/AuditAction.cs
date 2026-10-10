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
    SignedIn = 8,
    SignedOut = 9,

    /// <summary>A session was ended because the directory no longer allows the user in.</summary>
    AccessRevoked = 10,
}
