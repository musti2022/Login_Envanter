using System.Net;
using System.Text.Json;
using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Domain.Catalog;
using EnterpriseInventory.Domain.Organization;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Assets;
using EnterpriseInventory.IntegrationTests.Persistence;

namespace EnterpriseInventory.IntegrationTests.Dashboard;

/// <summary>
/// <c>GET /api/dashboard/statistics</c> against databases of their own, so every figure is known: five active
/// assets (two available, one assigned, one faulty, one retired) and one archived.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class DashboardStatisticsTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime, IDisposable
{
    private TestApiFactory? _api;
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        if (SqlServerDatabaseFixture.ServerConnectionString is null)
        {
            return;
        }

        _connectionString = await fixture.CreateMigratedDatabaseAsync();
        var model = AssetModel.Create(Brand.Create("Dell"), "Latitude 5440");
        var istanbul = City.Create("İstanbul");
        var ankara = City.Create("Ankara");
        var izmir = City.Create("İzmir");
        var it = Department.Create("Bilgi İşlem");
        var accounting = Department.Create("Muhasebe");
        var now = fixture.Clock.GetUtcNow();

        Asset New(string code, City city, Department department) => Asset.Create(code, AssetType.Laptop, model, city, department);
        var available = New("D-1", istanbul, it);
        var assigned = New("D-2", istanbul, accounting);
        assigned.Assign(PersistenceTestData.NewEmployee(now), "Zimmet", null, "mehmet.admin", now);
        var faulty = New("D-3", ankara, it);
        faulty.ChangeStatus(AssetStatus.Faulty);
        var retired = New("D-4", istanbul, it);
        retired.ChangeStatus(AssetStatus.Retired);
        var secondAvailable = New("D-5", ankara, accounting);
        var archived = New("D-0", izmir, accounting);
        archived.Archive();

        await using (var context = fixture.CreateContextFor(_connectionString))
        {
            context.AddRange(available, assigned, faulty, retired, secondAvailable, archived);
            await context.SaveChangesAsync();
        }

        _api = new TestApiFactory(_connectionString, settings: new Dictionary<string, string?> { ["RateLimiting:PermitLimit"] = "1000" });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _api?.Dispose();

    [SqlServerFact]
    public async Task The_counts_come_from_the_database()
    {
        using var client = _api!.CreateSignedInClient();

        var statistics = await client.GetOkAsync("/api/dashboard/statistics");

        Assert.Equal(5, statistics.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, statistics.GetProperty("assignedCount").GetInt32());
        Assert.Equal(2, statistics.GetProperty("availableCount").GetInt32());
        Assert.Equal(1, statistics.GetProperty("faultyCount").GetInt32());
        Assert.Equal(1, statistics.GetProperty("retiredCount").GetInt32());
        Assert.Equal(1, statistics.GetProperty("archivedCount").GetInt32());
    }

    [SqlServerFact]
    public async Task Cities_and_departments_are_counted_without_archived_assets_largest_first()
    {
        using var client = _api!.CreateSignedInClient();

        var statistics = await client.GetOkAsync("/api/dashboard/statistics");

        Assert.Equal([("İstanbul", 3), ("Ankara", 2)], Distribution(statistics, "byCity"));
        Assert.Equal([("Bilgi İşlem", 3), ("Muhasebe", 2)], Distribution(statistics, "byDepartment"));
    }

    [SqlServerFact]
    public async Task The_figures_follow_changes_and_recent_activity_lists_the_newest_asset_records_first()
    {
        using var client = _api!.CreateSignedInClient("ayse.admin");
        var before = await client.GetOkAsync("/api/dashboard/statistics");
        Assert.Empty(before.GetProperty("recentActivity").EnumerateArray());

        var references = await InventoryReferences.SeedAsync(fixture, _connectionString);
        var body = references.NewAssetBody("YENI-1");
        body["status"] = "Faulty";
        var created = await client.CreateAssetAsync(body);
        using (var archive = await client.SendWithCsrfAsync(
            HttpMethod.Delete, $"/api/assets/{created.Id()}?rowVersion={Uri.EscapeDataString(created.GetProperty("rowVersion").GetString()!)}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        }

        var second = await client.CreateAssetAsync(references.NewAssetBody("YENI-2"));
        var after = await client.GetOkAsync("/api/dashboard/statistics");

        Assert.Equal(6, after.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, after.GetProperty("availableCount").GetInt32());
        Assert.Equal(1, after.GetProperty("faultyCount").GetInt32());
        Assert.Equal(2, after.GetProperty("archivedCount").GetInt32());
        var activity = after.GetProperty("recentActivity").EnumerateArray().ToList();
        Assert.Equal(
            [("YENI-2", "Created"), ("YENI-1", "Archived"), ("YENI-1", "Created")],
            activity.Select(a => (a.GetProperty("assetCode").GetString(), a.GetProperty("action").GetString())));
        Assert.Equal(second.Id(), activity[0].GetProperty("assetId").GetInt32());
        Assert.All(activity, a => Assert.Equal("ayse.admin", a.GetProperty("userName").GetString()));
    }

    [SqlServerFact]
    public async Task Recent_activity_holds_the_last_ten_records()
    {
        using var client = _api!.CreateSignedInClient();
        var references = await InventoryReferences.SeedAsync(fixture, _connectionString);
        for (var i = 0; i < 12; i++)
        {
            await client.CreateAssetAsync(references.NewAssetBody($"SON-{i:00}"));
        }

        var statistics = await client.GetOkAsync("/api/dashboard/statistics");

        Assert.Equal(
            Enumerable.Range(2, 10).Reverse().Select(i => $"SON-{i:00}"),
            statistics.GetProperty("recentActivity").EnumerateArray().Select(a => a.GetProperty("assetCode").GetString()));
    }

    [Fact]
    public async Task Visitors_get_401_and_users_without_the_administrator_role_get_403()
    {
        await using var api = new TestApiFactory();
        using var visitor = api.CreateAnonymousClient();
        using var reader = api.CreateSignedInClient("veli.user", roles: "Reader");

        using var anonymous = await visitor.GetAsync(AssetApi.Uri("/api/dashboard/statistics"));
        using var forbidden = await reader.GetAsync(AssetApi.Uri("/api/dashboard/statistics"));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    private static List<(string?, int)> Distribution(JsonElement statistics, string property) =>
        statistics.GetProperty(property).EnumerateArray()
            .Select(d => (d.GetProperty("name").GetString(), d.GetProperty("count").GetInt32()))
            .ToList();
}
