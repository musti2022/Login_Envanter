using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Domain.Catalog;
using EnterpriseInventory.Domain.Organization;
using EnterpriseInventory.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseInventory.IntegrationTests.Assets;

/// <summary>
/// Lookups for asset tests, with unique names so tests sharing a database never meet: active ones to pick, and
/// inactive or mismatched ones to be refused.
/// </summary>
internal sealed record InventoryReferences(
    string BrandName,
    string ModelName,
    int ModelId,
    int SecondModelId,
    int InactiveModelId,
    int InactiveBrandModelId,
    int CityId,
    int SecondCityId,
    int InactiveCityId,
    int DepartmentId,
    int SecondDepartmentId,
    int InactiveDepartmentId,
    int LocationId,
    int SecondLocationId,
    int InactiveLocationId,
    int SecondCityLocationId)
{
    public static async Task<InventoryReferences> SeedAsync(SqlServerDatabaseFixture fixture, string? connectionString = null)
    {
        static string Unique(string prefix) => PersistenceTestData.Unique(prefix);

        var brand = Brand.Create(Unique("Marka"));
        var model = AssetModel.Create(brand, Unique("Model"));
        var secondModel = AssetModel.Create(brand, Unique("Model"));
        var inactiveModel = AssetModel.Create(brand, Unique("Pasif model"));
        inactiveModel.Deactivate();
        var inactiveBrand = Brand.Create(Unique("Pasif marka"));
        inactiveBrand.Deactivate();
        var inactiveBrandModel = AssetModel.Create(inactiveBrand, Unique("Model"));

        var city = City.Create(Unique("Şehir"));
        var secondCity = City.Create(Unique("Şehir"));
        var inactiveCity = City.Create(Unique("Pasif şehir"));
        inactiveCity.Deactivate();
        var location = Location.Create(city, Unique("Konum"));
        var secondLocation = Location.Create(city, Unique("Konum"));
        var inactiveLocation = Location.Create(city, Unique("Pasif konum"));
        inactiveLocation.Deactivate();
        var secondCityLocation = Location.Create(secondCity, Unique("Konum"));

        var department = Department.Create(Unique("Birim"));
        var secondDepartment = Department.Create(Unique("Birim"));
        var inactiveDepartment = Department.Create(Unique("Pasif birim"));
        inactiveDepartment.Deactivate();

        await using (var context = fixture.CreateContextFor(connectionString ?? fixture.ConnectionString))
        {
            context.AddRange(model, secondModel, inactiveModel, inactiveBrandModel);
            context.AddRange(location, secondLocation, inactiveLocation, secondCityLocation, inactiveCity);
            context.AddRange(department, secondDepartment, inactiveDepartment);
            await context.SaveChangesAsync();
        }

        return new InventoryReferences(
            brand.Name,
            model.Name,
            model.Id,
            secondModel.Id,
            inactiveModel.Id,
            inactiveBrandModel.Id,
            city.Id,
            secondCity.Id,
            inactiveCity.Id,
            department.Id,
            secondDepartment.Id,
            inactiveDepartment.Id,
            location.Id,
            secondLocation.Id,
            inactiveLocation.Id,
            secondCityLocation.Id);
    }

    /// <summary>A valid <c>POST /api/assets</c> body; tests change single fields to break it.</summary>
    public Dictionary<string, object?> NewAssetBody(string? assetCode = null) => new()
    {
        ["assetCode"] = assetCode ?? PersistenceTestData.Unique("DMR")[..30],
        ["assetType"] = "Laptop",
        ["modelId"] = ModelId,
        ["cityId"] = CityId,
        ["departmentId"] = DepartmentId,
        ["locationId"] = LocationId,
        ["computerName"] = "PC-TEST",
        ["serialNumber"] = PersistenceTestData.Unique("SN"),
        ["description"] = "Test demirbaşı",
    };

    /// <summary>
    /// Saves an asset on the active lookups straight to the database after <paramref name="prepare"/> (assigning or
    /// archiving it, which the API cannot do yet) and returns its ID. No audit record is written.
    /// </summary>
    public async Task<int> SaveAssetAsync(SqlServerDatabaseFixture fixture, Action<Asset> prepare)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(prepare);

        await using var db = fixture.CreateContext();
        var asset = Asset.Create(
            PersistenceTestData.Unique("DMR")[..30],
            AssetType.Desktop,
            await db.AssetModels.Include(m => m.Brand).SingleAsync(m => m.Id == ModelId),
            await db.Cities.SingleAsync(c => c.Id == CityId),
            await db.Departments.SingleAsync(d => d.Id == DepartmentId),
            computerName: "PC-SEED",
            serialNumber: PersistenceTestData.Unique("SN"));
        prepare(asset);
        db.Add(asset);
        await db.SaveChangesAsync();
        return asset.Id;
    }
}
