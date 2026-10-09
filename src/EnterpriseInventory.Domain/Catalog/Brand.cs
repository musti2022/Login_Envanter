using EnterpriseInventory.Domain.Common;

namespace EnterpriseInventory.Domain.Catalog;

public sealed class Brand : ReferenceDataEntity
{
    private Brand()
    {
    }

    private Brand(string name)
        : base(name)
    {
    }

    public static Brand Create(string name) => new(name);
}
