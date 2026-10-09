using EnterpriseInventory.Domain.Common;

namespace EnterpriseInventory.Domain.Catalog;

/// <summary>A model always belongs to exactly one brand; the name is unique within that brand.</summary>
public sealed class AssetModel : ReferenceDataEntity
{
    private AssetModel()
    {
    }

    private AssetModel(Brand brand, string name)
        : base(name)
    {
        Brand = brand;
        BrandId = brand.Id;
    }

    public int BrandId { get; private set; }

    public Brand Brand { get; private set; } = null!;

    public static AssetModel Create(Brand brand, string name)
    {
        ArgumentNullException.ThrowIfNull(brand);
        return new AssetModel(brand, name);
    }
}
