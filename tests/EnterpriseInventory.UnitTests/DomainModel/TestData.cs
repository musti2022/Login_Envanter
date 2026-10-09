using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Domain.Catalog;
using EnterpriseInventory.Domain.Employees;
using EnterpriseInventory.Domain.Organization;

namespace EnterpriseInventory.UnitTests.DomainModel;

/// <summary>Builds valid, unsaved domain objects for tests.</summary>
internal static class TestData
{
    public const string Admin = "admin.user";

    public static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.FromHours(3));

    public static AssetModel NewModel(string brand = "Dell", string model = "Latitude 5440") =>
        AssetModel.Create(Brand.Create(brand), model);

    public static City NewCity(string name = "İstanbul") => City.Create(name);

    public static Department NewDepartment(string name = "Bilgi İşlem") => Department.Create(name);

    public static Employee NewEmployee(bool isActive = true) =>
        Employee.Create(Guid.NewGuid(), "ayse.yilmaz", "Ayşe Yılmaz", "ayse.yilmaz@example.local", "Muhasebe", "Uzman", isActive, Now);

    public static Asset NewAsset(City? city = null, Location? location = null) =>
        Asset.Create(
            "DMR-0001",
            AssetType.Laptop,
            NewModel(),
            city ?? NewCity(),
            NewDepartment(),
            location,
            computerName: "PC-IST-01",
            serialNumber: "SN-123",
            description: "Muhasebe dizüstü");

    public static Asset NewAssignedAsset(out AssetAssignment assignment)
    {
        var asset = NewAsset();
        assignment = asset.Assign(NewEmployee(), "Dizüstü + şarj adaptörü", null, Admin, Now);
        return asset;
    }

    /// <summary>Simulates a database-generated key, as EF Core would set after saving.</summary>
    public static T WithId<T>(this T entity, int id)
        where T : EnterpriseInventory.Domain.Common.Entity
    {
        typeof(EnterpriseInventory.Domain.Common.Entity).GetProperty(nameof(EnterpriseInventory.Domain.Common.Entity.Id))!.SetValue(entity, id);
        return entity;
    }
}
