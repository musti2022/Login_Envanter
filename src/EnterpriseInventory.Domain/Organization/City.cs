using EnterpriseInventory.Domain.Common;

namespace EnterpriseInventory.Domain.Organization;

public sealed class City : ReferenceDataEntity
{
    private City()
    {
    }

    private City(string name)
        : base(name)
    {
    }

    public static City Create(string name) => new(name);
}
