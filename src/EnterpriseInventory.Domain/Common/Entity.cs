namespace EnterpriseInventory.Domain.Common;

/// <summary>Base type for entities identified by a database-generated integer key.</summary>
public abstract class Entity
{
    public int Id { get; protected set; }
}
