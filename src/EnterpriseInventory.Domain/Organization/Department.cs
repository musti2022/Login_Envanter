using EnterpriseInventory.Domain.Common;

namespace EnterpriseInventory.Domain.Organization;

public sealed class Department : ReferenceDataEntity
{
    private Department()
    {
    }

    private Department(string name)
        : base(name)
    {
    }

    public static Department Create(string name) => new(name);
}
