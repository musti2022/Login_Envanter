using EnterpriseInventory.Infrastructure.Persistence;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Assets;
using EnterpriseInventory.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseInventory.IntegrationTests.Performance;

/// <summary>
/// N+1 guard: every list sends the same, fixed number of SQL commands whether it shows two rows or a hundred, so
/// nothing is loaded row by row. A change that adds a query per row, or one more query per request, fails here.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class QueryCountTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime, IDisposable
{
    private readonly SqlMeter _meter = new();
    private TestApiFactory? _api;
    private HttpClient? _client;
    private int _busyAssetId;

    public async Task InitializeAsync()
    {
        if (SqlServerDatabaseFixture.ServerConnectionString is null)
        {
            return;
        }

        var connectionString = await fixture.SharedAsync("query-count-tests", async () =>
        {
            var database = await fixture.CreateMigratedDatabaseAsync();
            await LoadSeed.SeedAsync(database, assets: 150, employees: 40);
            return database;
        });

        await using (var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connectionString).Options))
        {
            // The asset with the longest history, so its history and assignment lists have several pages.
            _busyAssetId = await db.AssetAssignments.GroupBy(x => x.AssetId).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => g.Key).FirstAsync();
        }

        _api = new TestApiFactory(
            connectionString,
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

    public static TheoryData<string, string, int> Lists => new()
    {
        // Path with {0} for the page size, the items' property, and the commands every request sends.
        { "/api/assets?pageSize={0}", "items", 2 },
        { "/api/assets?pageSize={0}&sortBy=assignedDisplayName&sortDirection=desc", "items", 2 },
        { "/api/assets?pageSize={0}&search=Dizüstü", "items", 3 },
        { "/api/assets?pageSize={0}&archived=true", "items", 2 },
        { "/api/audit-logs?pageSize={0}", "items", 3 },
        { "/api/audit-logs?pageSize={0}&action=Assigned&action=Returned", "items", 3 },
        { "/api/reports/assignments?pageSize={0}", "items", 3 },
    };

    [SqlServerTheory]
    [MemberData(nameof(Lists))]
    public async Task A_list_sends_the_same_commands_for_two_rows_as_for_a_hundred(string path, string items, int commands)
    {
        var (fewRows, few) = await CountAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, path, 2), items);
        var (manyRows, many) = await CountAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, path, 100), items);

        Assert.True(manyRows > fewRows, $"{path}: the larger page should hold more rows ({fewRows} and {manyRows}).");
        // The texts can differ (a list of IDs has one parameter per ID), the number of commands cannot.
        Assert.Equal(few.Count, many.Count);
        Assert.Equal(commands, many.Count);
    }

    [SqlServerFact]
    public async Task An_assets_history_and_assignments_take_the_same_commands_for_one_entry_as_for_all()
    {
        foreach (var path in new[] { $"/api/assets/{_busyAssetId}/history?pageSize={{0}}", $"/api/assets/{_busyAssetId}/assignments?pageSize={{0}}" })
        {
            var (fewRows, few) = await CountAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, path, 1), "items");
            var (manyRows, many) = await CountAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, path, 100), "items");

            Assert.True(manyRows > fewRows, $"{path}: {fewRows} and {manyRows} rows.");
            Assert.Equal(few.Count, many.Count);
            Assert.Equal(3, many.Count);
        }
    }

    [SqlServerFact]
    public async Task Exports_read_every_row_in_one_query()
    {
        _meter.Start();
        using (var response = await _client!.GetAsync(AssetApi.Uri("/api/assets/export")))
        {
            Assert.True(response.IsSuccessStatusCode);
        }

        var assets = _meter.Stop();

        _meter.Start();
        using (var response = await _client.GetAsync(AssetApi.Uri("/api/reports/assignments/export")))
        {
            Assert.True(response.IsSuccessStatusCode);
        }

        var movements = _meter.Stop();

        // A count, then the rows (the movements also name the filters they used).
        Assert.Equal(2, assets.Count);
        Assert.Equal(3, movements.Count);
    }

    [SqlServerFact]
    public async Task The_dashboard_the_summary_and_an_asset_take_a_fixed_number_of_commands()
    {
        _meter.Start();
        await _client!.GetOkAsync("/api/dashboard/statistics");
        var dashboard = _meter.Stop();

        _meter.Start();
        await _client!.GetOkAsync("/api/reports/asset-summary?groupBy=location");
        var summary = _meter.Stop();

        _meter.Start();
        await _client!.GetOkAsync($"/api/assets/{_busyAssetId}");
        var asset = _meter.Stop();

        Assert.Equal(9, dashboard.Count);
        Assert.Single(summary);
        Assert.Single(asset);
    }

    private async Task<(int Rows, IReadOnlyList<string> Commands)> CountAsync(string path, string items)
    {
        _meter.Start();
        var body = await _client!.GetOkAsync(path);
        var commands = _meter.Stop();
        return (body.GetProperty(items).GetArrayLength(), commands);
    }
}
