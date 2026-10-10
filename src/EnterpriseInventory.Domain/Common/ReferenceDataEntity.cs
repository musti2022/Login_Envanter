namespace EnterpriseInventory.Domain.Common;

/// <summary>
/// Named lookup data (brand, model, city, department, location). Lookups are never deleted because
/// assets and history refer to them; they are deactivated instead so they cannot be chosen for new records.
/// </summary>
public abstract class ReferenceDataEntity : AuditableEntity
{
    public const int NameMaxLength = 100;

    protected ReferenceDataEntity()
    {
    }

    protected ReferenceDataEntity(string name)
    {
        Name = Guard.Required(name, NameMaxLength, nameof(name));
    }

    public string Name { get; private set; } = string.Empty;

    public bool IsActive { get; private set; } = true;

    public void Rename(string name) => Name = Guard.Required(name, NameMaxLength, nameof(name));

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
