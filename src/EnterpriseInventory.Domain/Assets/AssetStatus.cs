namespace EnterpriseInventory.Domain.Assets;

/// <summary>Lifecycle state of an asset. Values are stored in the database; never renumber existing members.</summary>
public enum AssetStatus
{
    /// <summary>In stock and free to assign ("Boşta").</summary>
    Available = 1,

    /// <summary>Has exactly one active assignment ("Zimmetli"). Only set through <see cref="Asset.Assign"/>.</summary>
    Assigned = 2,

    /// <summary>Broken or waiting for repair ("Arızalı").</summary>
    Faulty = 3,

    /// <summary>Out of service, kept for history ("Hurda").</summary>
    Retired = 4,
}
