using EnterpriseInventory.Domain.Assets;
using Microsoft.EntityFrameworkCore;
using static EnterpriseInventory.IntegrationTests.Persistence.PersistenceTestData;

namespace EnterpriseInventory.IntegrationTests.Persistence;

/// <summary>Saves and loads the asset aggregate through EF Core against a real SQL Server.</summary>
[Collection(SqlServerTestGroup.Name)]
public class AssetPersistenceTests(SqlServerDatabaseFixture database)
{
    [SqlServerFact]
    public async Task Saved_asset_round_trips_with_audit_fields_and_a_row_version()
    {
        var asset = await database.SaveAsync(NewAsset(serialNumber: Unique("SN")), "ayse.admin");

        await using var context = database.CreateContext();
        var loaded = await context.Assets
            .Include(a => a.Model).ThenInclude(m => m.Brand)
            .Include(a => a.Location)
            .SingleAsync(a => a.Id == asset.Id);

        Assert.Equal(asset.AssetCode, loaded.AssetCode);
        Assert.Equal(asset.SerialNumber, loaded.SerialNumber);
        Assert.Equal(AssetType.Laptop, loaded.AssetType);
        Assert.Equal(AssetStatus.Available, loaded.Status);
        Assert.Equal(loaded.Model.BrandId, loaded.BrandId);
        Assert.Equal(loaded.CityId, loaded.Location!.CityId);
        Assert.Equal("ayse.admin", loaded.CreatedBy);
        Assert.Equal(database.Clock.GetUtcNow(), loaded.CreatedAt);
        Assert.Null(loaded.UpdatedAt);
        Assert.NotEmpty(loaded.RowVersion);
        Assert.Equal("ayse.admin", loaded.Model.Brand.CreatedBy);
    }

    [SqlServerFact]
    public async Task Update_stamps_UpdatedAt_and_UpdatedBy_and_changes_the_row_version()
    {
        var asset = await database.SaveAsync(NewAsset());
        database.Clock.Advance(TimeSpan.FromHours(1));

        await using (var context = database.CreateContext("mehmet.admin"))
        {
            var tracked = await context.Assets.SingleAsync(a => a.Id == asset.Id);
            tracked.ChangeStatus(AssetStatus.Faulty);
            await context.SaveChangesAsync();
        }

        await using var reader = database.CreateContext();
        var loaded = await reader.Assets.SingleAsync(a => a.Id == asset.Id);
        Assert.Equal(AssetStatus.Faulty, loaded.Status);
        Assert.Equal("mehmet.admin", loaded.UpdatedBy);
        Assert.Equal(database.Clock.GetUtcNow(), loaded.UpdatedAt);
        Assert.Equal(SqlServerDatabaseFixture.DefaultUser, loaded.CreatedBy);
        Assert.Equal(asset.CreatedAt, loaded.CreatedAt);
        Assert.NotEqual(asset.RowVersion, loaded.RowVersion);
    }

    [SqlServerFact]
    public async Task Saving_a_stale_copy_throws_a_concurrency_exception_and_keeps_the_first_change()
    {
        var asset = await database.SaveAsync(NewAsset());
        await using var first = database.CreateContext("birinci.admin");
        await using var second = database.CreateContext("ikinci.admin");
        var firstCopy = await first.Assets.SingleAsync(a => a.Id == asset.Id);
        var secondCopy = await second.Assets.SingleAsync(a => a.Id == asset.Id);

        firstCopy.ChangeStatus(AssetStatus.Faulty);
        await first.SaveChangesAsync();
        secondCopy.ChangeStatus(AssetStatus.Retired);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var reader = database.CreateContext();
        var loaded = await reader.Assets.SingleAsync(a => a.Id == asset.Id);
        Assert.Equal(AssetStatus.Faulty, loaded.Status);
        Assert.Equal("birinci.admin", loaded.UpdatedBy);
    }

    [SqlServerFact]
    public async Task Two_admins_assigning_the_same_asset_at_once_leave_one_active_assignment()
    {
        var asset = await database.SaveAsync(NewAsset());
        var employeeA = await database.SaveAsync(NewEmployee(database.Clock.GetUtcNow()));
        var employeeB = await database.SaveAsync(NewEmployee(database.Clock.GetUtcNow()));
        await using var first = database.CreateContext("birinci.admin");
        await using var second = database.CreateContext("ikinci.admin");
        var firstCopy = await first.Assets.Include(a => a.Assignments).SingleAsync(a => a.Id == asset.Id);
        var secondCopy = await second.Assets.Include(a => a.Assignments).SingleAsync(a => a.Id == asset.Id);
        var now = database.Clock.GetUtcNow();

        firstCopy.Assign(await first.Employees.SingleAsync(e => e.Id == employeeA.Id), "Dizüstü", null, "birinci.admin", now);
        await first.SaveChangesAsync();
        secondCopy.Assign(await second.Employees.SingleAsync(e => e.Id == employeeB.Id), "Dizüstü", null, "ikinci.admin", now);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => second.SaveChangesAsync());
        await using var reader = database.CreateContext();
        var assignments = await reader.AssetAssignments.Where(x => x.AssetId == asset.Id).ToListAsync();
        var assignment = Assert.Single(assignments);
        Assert.Equal(employeeA.Id, assignment.EmployeeId);
    }

    [SqlServerFact]
    public async Task Returned_asset_can_be_assigned_again_in_one_save_and_the_history_is_kept()
    {
        var asset = await database.SaveAsync(NewAsset());
        var employeeA = await database.SaveAsync(NewEmployee(database.Clock.GetUtcNow()));
        var employeeB = await database.SaveAsync(NewEmployee(database.Clock.GetUtcNow()));
        var assignedAt = database.Clock.GetUtcNow();
        await using (var context = database.CreateContext())
        {
            var tracked = await context.Assets.Include(a => a.Assignments).SingleAsync(a => a.Id == asset.Id);
            tracked.Assign(await context.Employees.SingleAsync(e => e.Id == employeeA.Id), "Dizüstü", null, "test.admin", assignedAt);
            await context.SaveChangesAsync();
        }

        database.Clock.Advance(TimeSpan.FromDays(30));
        var handoverAt = database.Clock.GetUtcNow();
        await using (var context = database.CreateContext())
        {
            var tracked = await context.Assets.Include(a => a.Assignments).SingleAsync(a => a.Id == asset.Id);
            tracked.Return("test.admin", handoverAt);
            tracked.Assign(await context.Employees.SingleAsync(e => e.Id == employeeB.Id), "Dizüstü", null, "test.admin", handoverAt);
            await context.SaveChangesAsync();
        }

        await using var reader = database.CreateContext();
        var loaded = await reader.Assets.Include(a => a.Assignments).SingleAsync(a => a.Id == asset.Id);
        Assert.Equal(AssetStatus.Assigned, loaded.Status);
        Assert.Equal(2, loaded.Assignments.Count);
        var previous = loaded.Assignments.Single(x => x.EmployeeId == employeeA.Id);
        Assert.Equal(assignedAt, previous.AssignedAt);
        Assert.Equal(handoverAt, previous.ReturnedAt);
        Assert.Equal("test.admin", previous.ReturnedBy);
        Assert.Equal(employeeB.Id, loaded.ActiveAssignment?.EmployeeId);
    }

    [SqlServerFact]
    public async Task Clearing_the_location_keeps_the_city()
    {
        var asset = await database.SaveAsync(NewAsset(withLocation: true));

        await using (var context = database.CreateContext())
        {
            var tracked = await context.Assets
                .Include(a => a.City).Include(a => a.Department).Include(a => a.Location)
                .SingleAsync(a => a.Id == asset.Id);
            Assert.True(tracked.ChangeLocation(tracked.City, tracked.Department, location: null));
            await context.SaveChangesAsync();
        }

        await using var reader = database.CreateContext();
        var loaded = await reader.Assets.SingleAsync(a => a.Id == asset.Id);
        Assert.Null(loaded.LocationId);
        Assert.Equal(asset.CityId, loaded.CityId);
    }

    [SqlServerFact]
    public async Task Serial_numbers_are_unique_but_many_assets_may_have_none()
    {
        await database.SaveAsync(NewAsset(serialNumber: null));
        await database.SaveAsync(NewAsset(serialNumber: null));
        var serialNumber = Unique("SN");
        await database.SaveAsync(NewAsset(serialNumber: serialNumber));

        await AssertViolatesAsync("IX_Assets_SerialNumber", () => database.SaveAsync(NewAsset(serialNumber: serialNumber)));
    }

    [SqlServerFact]
    public async Task Asset_codes_are_unique()
    {
        var assetCode = Unique("DMR");
        await database.SaveAsync(NewAsset(assetCode));

        await AssertViolatesAsync("IX_Assets_AssetCode", () => database.SaveAsync(NewAsset(assetCode)));
    }
}
