using EnterpriseInventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static EnterpriseInventory.IntegrationTests.Persistence.PersistenceTestData;

namespace EnterpriseInventory.IntegrationTests.Persistence;

/// <summary>
/// Archiving is a soft delete: the global query filter hides an archived asset and its assignments from every
/// query, the rows stay, and <see cref="SoftDelete.IncludingArchived{T}"/> reads them on purpose.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public class SoftDeleteTests(SqlServerDatabaseFixture database)
{
    [SqlServerFact]
    public async Task An_archived_asset_and_its_assignments_are_left_out_of_normal_queries_but_kept()
    {
        var assignedAt = database.Clock.GetUtcNow();
        var archived = NewAsset();
        archived.Assign(NewEmployee(assignedAt), "Eski zimmet", notes: null, "test.admin", assignedAt);
        archived.Return("test.admin", assignedAt.AddHours(1));
        archived.Archive();
        var active = NewAsset();
        active.Assign(NewEmployee(assignedAt), "Güncel zimmet", notes: null, "test.admin", assignedAt);
        await database.SaveAllAsync(archived, active);
        int[] ids = [archived.Id, active.Id];

        await using var db = database.CreateContext();

        Assert.Equal([active.Id], await db.Assets.Where(a => ids.Contains(a.Id)).Select(a => a.Id).ToListAsync());
        Assert.Null(await db.Assets.FindAsync(archived.Id));
        Assert.Equal([active.Id], await db.AssetAssignments.Where(x => ids.Contains(x.AssetId)).Select(x => x.AssetId).ToListAsync());

        var kept = await db.Assets.IncludingArchived().Include(a => a.Assignments).SingleAsync(a => a.Id == archived.Id);
        Assert.True(kept.IsDeleted);
        Assert.Equal("Eski zimmet", Assert.Single(kept.Assignments).AssignmentDescription);
        Assert.Equal(2, await db.AssetAssignments.IncludingArchived().CountAsync(x => ids.Contains(x.AssetId)));
    }

    [SqlServerFact]
    public async Task The_filter_runs_in_SQL()
    {
        await using var db = database.CreateContext();

        var sql = db.Assets.Where(a => a.AssetCode == "x").ToQueryString();

        Assert.Contains("[IsDeleted] = CAST(0 AS bit)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE", db.Assets.IncludingArchived().ToQueryString(), StringComparison.Ordinal);
    }

    [SqlServerFact]
    public async Task An_archived_asset_keeps_its_code_and_serial_number()
    {
        var archived = NewAsset(serialNumber: Unique("SN"));
        archived.Archive();
        await database.SaveAsync(archived);

        await AssertViolatesAsync("IX_Assets_AssetCode", () => database.SaveAsync(NewAsset(assetCode: archived.AssetCode)));
        await AssertViolatesAsync("IX_Assets_SerialNumber", () => database.SaveAsync(NewAsset(serialNumber: archived.SerialNumber)));
    }
}
