using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Domain.Catalog;
using EnterpriseInventory.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using static EnterpriseInventory.IntegrationTests.Persistence.PersistenceTestData;

namespace EnterpriseInventory.IntegrationTests.Persistence;

/// <summary>
/// The database rejects invalid data even when it bypasses the domain model (raw SQL, a bug, a manual fix).
/// Each test writes the invalid change directly and expects SQL Server to name the constraint it broke.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public class DatabaseConstraintTests(SqlServerDatabaseFixture database)
{
    [SqlServerFact]
    public async Task Model_of_another_brand_is_rejected()
    {
        var asset = await database.SaveAsync(NewAsset());
        var otherBrandId = (await database.SaveAsync(NewAsset())).BrandId;
        await using var context = database.CreateContext();

        await AssertViolatesAsync("FK_Assets_AssetModels_ModelId_BrandId", () =>
            context.Database.ExecuteSqlAsync($"UPDATE [Assets] SET [BrandId] = {otherBrandId} WHERE [Id] = {asset.Id}"));
    }

    [SqlServerFact]
    public async Task Location_in_another_city_is_rejected()
    {
        var asset = await database.SaveAsync(NewAsset());
        var otherLocationId = (await database.SaveAsync(NewAsset(withLocation: true))).LocationId;
        await using var context = database.CreateContext();

        await AssertViolatesAsync("FK_Assets_Locations_LocationId_CityId", () =>
            context.Database.ExecuteSqlAsync($"UPDATE [Assets] SET [LocationId] = {otherLocationId} WHERE [Id] = {asset.Id}"));
    }

    [SqlServerFact]
    public async Task Unknown_status_or_type_and_blank_code_or_serial_number_are_rejected()
    {
        var asset = await database.SaveAsync(NewAsset(serialNumber: Unique("SN")));
        await using var context = database.CreateContext();

        await AssertViolatesAsync("CK_Assets_Status", () =>
            context.Database.ExecuteSqlAsync($"UPDATE [Assets] SET [Status] = {99} WHERE [Id] = {asset.Id}"));
        await AssertViolatesAsync("CK_Assets_AssetType", () =>
            context.Database.ExecuteSqlAsync($"UPDATE [Assets] SET [AssetType] = {50} WHERE [Id] = {asset.Id}"));
        await AssertViolatesAsync("CK_Assets_AssetCode_NotBlank", () =>
            context.Database.ExecuteSqlAsync($"UPDATE [Assets] SET [AssetCode] = {"   "} WHERE [Id] = {asset.Id}"));
        await AssertViolatesAsync("CK_Assets_SerialNumber_NotBlank", () =>
            context.Database.ExecuteSqlAsync($"UPDATE [Assets] SET [SerialNumber] = {"   "} WHERE [Id] = {asset.Id}"));
    }

    [SqlServerFact]
    public async Task Audit_records_need_a_known_action_and_old_or_new_values()
    {
        await using var context = database.CreateContext();
        var now = database.Clock.GetUtcNow();

        await AssertViolatesAsync("CK_AuditLogs_Action", () =>
            context.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO [AuditLogs] ([EntityName], [EntityId], [Action], [NewValues], [UserName], [Timestamp], [CorrelationId])
                VALUES ({"Asset"}, {"1"}, {99}, {"{}"}, {"test.admin"}, {now}, {"c-1"})
                """));
        await AssertViolatesAsync("CK_AuditLogs_HasValues", () =>
            context.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO [AuditLogs] ([EntityName], [EntityId], [Action], [UserName], [Timestamp], [CorrelationId])
                VALUES ({"Asset"}, {"1"}, {1}, {"test.admin"}, {now}, {"c-1"})
                """));
    }

    [SqlServerFact]
    public async Task Assigned_asset_cannot_be_archived_and_cannot_get_a_second_active_assignment()
    {
        var (asset, assignment) = await SaveAssignedAssetAsync();
        await using var context = database.CreateContext();

        await AssertViolatesAsync("CK_Assets_ArchivedNotAssigned", () =>
            context.Database.ExecuteSqlAsync($"UPDATE [Assets] SET [IsDeleted] = {true} WHERE [Id] = {asset.Id}"));
        await AssertViolatesAsync("UX_AssetAssignments_AssetId_Active", () =>
            context.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO [AssetAssignments] ([AssetId], [EmployeeId], [AssignedAt], [AssignedBy])
                VALUES ({asset.Id}, {assignment.EmployeeId}, {database.Clock.GetUtcNow()}, {"test.admin"})
                """));
    }

    [SqlServerFact]
    public async Task Return_must_not_precede_the_assignment_and_needs_the_returning_user()
    {
        var (_, assignment) = await SaveAssignedAssetAsync();
        var tooEarly = assignment.AssignedAt.AddDays(-1);
        await using var context = database.CreateContext();

        await AssertViolatesAsync("CK_AssetAssignments_ReturnAfterAssign", () =>
            context.Database.ExecuteSqlAsync(
                $"UPDATE [AssetAssignments] SET [ReturnedAt] = {tooEarly}, [ReturnedBy] = {"test.admin"} WHERE [Id] = {assignment.Id}"));
        await AssertViolatesAsync("CK_AssetAssignments_ReturnedByWithReturn", () =>
            context.Database.ExecuteSqlAsync(
                $"UPDATE [AssetAssignments] SET [ReturnedBy] = {"test.admin"} WHERE [Id] = {assignment.Id}"));
    }

    [SqlServerFact]
    public async Task Lookup_names_are_unique_with_Turkish_case_rules()
    {
        // Under Turkish_CI_AS "izmir" and "İZMİR" are the same name; other collations treat them as different.
        var suffix = Guid.NewGuid().ToString("N");
        await database.SaveAsync(City.Create($"izmir {suffix}"));

        await AssertViolatesAsync("IX_Cities_Name", () =>
            database.SaveAsync(City.Create($"İZMİR {suffix.ToUpperInvariant()}")));
    }

    [SqlServerFact]
    public async Task Brand_and_department_names_are_unique()
    {
        var brandName = Unique("Marka");
        var departmentName = Unique("Birim");
        await database.SaveAllAsync(Brand.Create(brandName), Department.Create(departmentName));

        await AssertViolatesAsync("IX_Brands_Name", () => database.SaveAsync(Brand.Create(brandName)));
        await AssertViolatesAsync("IX_Departments_Name", () => database.SaveAsync(Department.Create(departmentName)));
    }

    [SqlServerFact]
    public async Task Model_names_are_unique_within_a_brand_only()
    {
        var modelName = Unique("Model");
        await database.SaveAllAsync(
            AssetModel.Create(Brand.Create(Unique("Marka")), modelName),
            AssetModel.Create(Brand.Create(Unique("Marka")), modelName));
        var brand = Brand.Create(Unique("Marka"));

        await AssertViolatesAsync("IX_AssetModels_BrandId_Name", () =>
            database.SaveAllAsync(AssetModel.Create(brand, modelName), AssetModel.Create(brand, modelName)));
    }

    [SqlServerFact]
    public async Task Location_names_are_unique_within_a_city_only()
    {
        var locationName = Unique("Konum");
        await database.SaveAllAsync(
            Location.Create(City.Create(Unique("Şehir")), locationName),
            Location.Create(City.Create(Unique("Şehir")), locationName));
        var city = City.Create(Unique("Şehir"));

        await AssertViolatesAsync("IX_Locations_CityId_Name", () =>
            database.SaveAllAsync(Location.Create(city, locationName), Location.Create(city, locationName)));
    }

    private async Task<(Asset Asset, AssetAssignment Assignment)> SaveAssignedAssetAsync()
    {
        var asset = NewAsset();
        var assignment = asset.Assign(NewEmployee(database.Clock.GetUtcNow()), "Dizüstü", null, "test.admin", database.Clock.GetUtcNow());
        await database.SaveAsync(asset);
        return (asset, assignment);
    }
}
