using System.Globalization;
using EnterpriseInventory.Infrastructure.Assets;
using EnterpriseInventory.Infrastructure.Persistence;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Assets;
using EnterpriseInventory.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseInventory.IntegrationTests.Performance;

/// <summary>
/// A search that matches at most <see cref="AssetStore.MaxListedMatches"/> records reaches the list query as their
/// IDs; a broader one stays in the query. Both ways must find the same records, counted here straight from the tables.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class SearchMatchLimitTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime, IDisposable
{
    // Of 3,000 assets, "1" is in the codes of 1,542 and "Dizüstü" in the assignments of about 2,100: more than are
    // listed, and not all, so the query must still filter. "DMR-0001" is in a hundred codes (DMR-000100 to 199).
    private const int Assets = 3000;
    private const string Narrow = "DMR-0001";

    private readonly SqlMeter _meter = new();
    private string _connectionString = string.Empty;
    private TestApiFactory? _api;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        if (SqlServerDatabaseFixture.ServerConnectionString is null)
        {
            return;
        }

        _connectionString = await fixture.SharedAsync("search-match-limit-tests", async () =>
        {
            var database = await fixture.CreateMigratedDatabaseAsync();
            await LoadSeed.SeedAsync(database, assets: Assets, employees: 60);
            return database;
        });

        _api = new TestApiFactory(
            _connectionString,
            settings: new Dictionary<string, string?>(LoginEndpointTests.FakeDirectory) { ["RateLimiting:PermitLimit"] = "1000" },
            configureServices: services =>
            {
                services.AddSingleton<TimeProvider>(new TestClock(LoadSeed.Now));
                services.ConfigureDbContext<ApplicationDbContext>(options => options.AddInterceptors(_meter));
            });
        _client = _api.CreateSignedInClient();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _client?.Dispose();
        _api?.Dispose();
    }

    [SqlServerTheory]
    [InlineData("Dizüstü", false)]
    [InlineData(Narrow, true)]
    public async Task The_inventory_search_finds_the_same_assets_either_way(string term, bool listed)
    {
        await using var db = Context();
        // Only codes hold "DMR-" and only assignment descriptions "Dizüstü"; the seed names nothing else so.
        var matching = db.Assets.Where(a => a.AssetCode.Contains(term) || a.Assignments.Any(x => x.ReturnedAt == null && x.AssignmentDescription!.Contains(term)));
        var expected = await matching.CountAsync();

        var (total, commands) = await TotalAsync($"/api/assets?pageSize=5&search={term}");

        Assert.InRange(expected, 1, Assets - 1);
        Assert.Equal(expected, total);
        AssertWay(listed, expected, commands);
    }

    [SqlServerFact]
    public async Task The_archive_search_finds_the_same_assets_either_way()
    {
        await using var db = Context();
        var archived = db.Assets.IgnoreQueryFilters().Where(a => a.IsDeleted);

        // "DMR-0" is in every code (more than are listed); "DMR-001" in exactly as many as are listed, DMR-001000 to
        // 1999, archived or not: the IDs listed for the archive must include archived assets.
        var (broad, broadCommands) = await TotalAsync("/api/assets?pageSize=5&archived=true&search=DMR-0");
        var (narrow, narrowCommands) = await TotalAsync("/api/assets?pageSize=5&archived=true&search=DMR-001");

        Assert.Equal(await archived.CountAsync(), broad);
        AssertWay(listed: false, Assets, broadCommands);
        var expected = await archived.CountAsync(a => a.AssetCode.Contains("DMR-001"));
        Assert.InRange(expected, 1, broad - 1);
        Assert.Equal(expected, narrow);
        AssertWay(listed: true, await db.Assets.IgnoreQueryFilters().CountAsync(a => a.AssetCode.Contains("DMR-001")), narrowCommands);
    }

    [SqlServerTheory]
    [InlineData("1", false)]
    [InlineData(Narrow, true)]
    public async Task The_movement_search_finds_the_same_movements_either_way(string term, bool listed)
    {
        await using var db = Context();
        var assignments = db.AssetAssignments.IgnoreQueryFilters()
            .Where(x => x.Asset.AssetCode.Contains(term) || x.Employee.SamAccountName.Contains(term) || x.Employee.DisplayName.Contains(term));
        var expected = await assignments.CountAsync() + await assignments.CountAsync(x => x.ReturnedAt != null);

        var (total, commands) = await TotalAsync($"/api/reports/assignments?pageSize=5&search={term}");

        Assert.InRange(await assignments.CountAsync(), 1, await db.AssetAssignments.IgnoreQueryFilters().CountAsync() - 1);
        Assert.Equal(expected, total);
        AssertWay(listed, await db.Assets.IgnoreQueryFilters().CountAsync(a => a.AssetCode.Contains(term)), commands);
    }

    [SqlServerTheory]
    [InlineData("1", false)]
    [InlineData(Narrow, true)]
    public async Task The_audit_log_finds_the_same_records_by_asset_code_either_way(string term, bool listed)
    {
        await using var db = Context();
        var ids = (await db.Assets.IgnoreQueryFilters().Where(a => a.AssetCode.Contains(term)).Select(a => a.Id).ToListAsync())
            .ConvertAll(id => id.ToString(CultureInfo.InvariantCulture))
            .ToHashSet(StringComparer.Ordinal);
        var assetRecords = await db.AuditLogs.Where(l => l.EntityName == "Asset").Select(l => l.EntityId).ToListAsync();
        var expected = assetRecords.Count(ids.Contains);

        var (total, commands) = await TotalAsync($"/api/audit-logs?pageSize=5&assetCode={term}");

        Assert.InRange(expected, 1, assetRecords.Count - 1);
        Assert.Equal(expected, total);
        AssertWay(listed, ids.Count, commands);
    }

    /// <summary>Listed IDs leave no LIKE in the (first) count; a broad search keeps it there.</summary>
    private static void AssertWay(bool listed, int matches, IReadOnlyList<string> commands)
    {
        Assert.Equal(listed, matches <= AssetStore.MaxListedMatches);
        var count = commands.First(c => c.Contains("COUNT(", StringComparison.Ordinal));
        Assert.Equal(!listed, count.Contains("LIKE", StringComparison.Ordinal));
    }

    private async Task<(int Total, IReadOnlyList<string> Commands)> TotalAsync(string path)
    {
        _meter.Start();
        var body = await _client!.GetOkAsync(path);
        return (body.GetProperty("totalCount").GetInt32(), _meter.Stop());
    }

    private ApplicationDbContext Context() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(_connectionString).Options);
}
