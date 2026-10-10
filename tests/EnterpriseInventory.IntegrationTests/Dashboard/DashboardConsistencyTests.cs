using System.Globalization;
using System.Net;
using System.Text.Json;
using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Domain.Catalog;
using EnterpriseInventory.Domain.Organization;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Assets;
using EnterpriseInventory.IntegrationTests.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseInventory.IntegrationTests.Dashboard;

/// <summary>
/// Every figure of <c>GET /api/dashboard/statistics</c> against the same figure counted with plain SQL on the same
/// database: statuses, cities, departments, types, brands and the monthly assignments and returns (months counted by
/// SQL Server's own <c>AT TIME ZONE</c>, independently of the API's time zone code).
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class DashboardConsistencyTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime, IDisposable
{
    /// <summary>9 October 2026, 09:00 in Istanbul: the window runs from November 2025 to October 2026.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 6, 0, 0, TimeSpan.Zero);

    private static readonly string[] CityNames = ["İstanbul", "Ankara", "İzmir", "Bursa", "Çorum", "Şanlıurfa", "Iğdır", "Uşak", "Ordu", "Çanakkale"];
    private static readonly string[] DepartmentNames = ["Bilgi İşlem", "Muhasebe", "İnsan Kaynakları", "Satın Alma", "Hukuk", "Üretim", "Lojistik", "Satış", "Çağrı Merkezi"];
    private static readonly string[] DistributionNames = ["byCity", "byDepartment", "byBrand"];

    private TestApiFactory? _api;
    private string _connectionString = string.Empty;
    private readonly TestClock _clock = new(Now);

    public async Task InitializeAsync()
    {
        if (SqlServerDatabaseFixture.ServerConnectionString is null)
        {
            return;
        }

        _connectionString = await fixture.CreateMigratedDatabaseAsync();
        await SeedAsync();
        _api = new TestApiFactory(
            _connectionString,
            settings: new Dictionary<string, string?>(LoginEndpointTests.FakeDirectory) { ["RateLimiting:PermitLimit"] = "1000" },
            configureServices: services => services.AddSingleton<TimeProvider>(_clock));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _api?.Dispose();

    /// <summary>
    /// 22 assets on ten brands (6, 5, 4 and seven with one each), ten cities, nine departments, four types and every
    /// status; two archived; assignments and returns on both sides of month boundaries in Istanbul. More than eight
    /// cities, departments and brands, so every distribution counts some of them together.
    /// </summary>
    private async Task SeedAsync()
    {
        var brands = Enumerable.Range(1, 10).Select(i => Brand.Create($"Marka {i:D2}")).ToList();
        var models = brands.ConvertAll(b => AssetModel.Create(b, "Model"));
        var cities = CityNames.Select(City.Create).ToList();
        var departments = DepartmentNames.Select(Department.Create).ToList();
        AssetType[] types = [AssetType.Laptop, AssetType.Desktop, AssetType.Monitor, AssetType.Laptop, AssetType.Printer];

        var assets = new List<Asset>();
        var brandOf = new[] { 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 2, 2, 2, 2, 3, 4, 5, 6, 7, 8, 9 };
        for (var i = 0; i < brandOf.Length; i++)
        {
            assets.Add(Asset.Create($"T-{i:D2}", types[i % types.Length], models[brandOf[i]], cities[i % cities.Count], departments[i % departments.Count]));
        }

        var employee = PersistenceTestData.NewEmployee(Now);
        DateTimeOffset At(string moment) => DateTimeOffset.Parse(moment, CultureInfo.InvariantCulture);

        // Returned in November 2025 (inside the window) after an assignment from before it.
        assets[0].Assign(employee, null, null, "ayse.admin", At("2025-10-15T09:00:00Z"));
        assets[0].Return("ayse.admin", At("2025-11-02T09:00:00Z"));

        // 23:59 on 31 October in Istanbul is outside the window; one minute later, 1 November, is inside.
        assets[1].Assign(employee, null, null, "ayse.admin", At("2025-10-31T20:59:00Z"));
        assets[2].Assign(employee, null, null, "ayse.admin", At("2025-10-31T21:00:00Z"));

        // 23:30 on 30 September in Istanbul is September; 00:30 on 1 October is October, though both are 30 September in UTC.
        assets[3].Assign(employee, null, null, "ayse.admin", At("2026-09-30T20:30:00Z"));
        assets[3].Return("ayse.admin", At("2026-10-05T08:00:00Z"));
        assets[4].Assign(employee, null, null, "ayse.admin", At("2026-09-30T21:30:00Z"));

        // Assigned, returned and assigned again.
        assets[5].Assign(employee, null, null, "ayse.admin", At("2026-03-10T08:00:00Z"));
        assets[5].Return("ayse.admin", At("2026-03-20T08:00:00Z"));
        assets[5].Assign(employee, null, null, "ayse.admin", At("2026-04-01T08:00:00Z"));

        // Assigned before the window, in Uşak, a city counted with the others.
        assets[17].Assign(employee, null, null, "ayse.admin", At("2025-06-02T09:00:00Z"));

        assets[6].ChangeStatus(AssetStatus.Faulty);
        assets[7].ChangeStatus(AssetStatus.Faulty);
        assets[8].ChangeStatus(AssetStatus.Retired);

        // Archived after its assignment was returned: its movements still count, the asset no longer does.
        assets[9].Assign(employee, null, null, "ayse.admin", At("2026-01-05T08:00:00Z"));
        assets[9].Return("ayse.admin", At("2026-02-01T08:00:00Z"));
        assets[9].Archive();
        assets[10].Archive();

        await using var context = fixture.CreateContextFor(_connectionString);
        context.AddRange(assets);
        await context.SaveChangesAsync();
    }

    [SqlServerFact]
    public async Task Every_figure_matches_the_database_counted_with_plain_SQL()
    {
        using var client = _api!.CreateSignedInClient();

        var statistics = await client.GetOkAsync("/api/dashboard/statistics");

        // Statuses.
        var byStatus = await QueryAsync("SELECT IsDeleted, Status, COUNT(*) FROM dbo.Assets GROUP BY IsDeleted, Status", r => (Archived: r.GetBoolean(0), Status: r.GetInt32(1), Count: r.GetInt32(2)));
        int Active(AssetStatus status) => byStatus.Where(s => !s.Archived && s.Status == (int)status).Sum(s => s.Count);
        Assert.Equal(byStatus.Where(s => !s.Archived).Sum(s => s.Count), statistics.GetProperty("totalCount").GetInt32());
        Assert.Equal(Active(AssetStatus.Assigned), statistics.GetProperty("assignedCount").GetInt32());
        Assert.Equal(Active(AssetStatus.Available), statistics.GetProperty("availableCount").GetInt32());
        Assert.Equal(Active(AssetStatus.Faulty), statistics.GetProperty("faultyCount").GetInt32());
        Assert.Equal(Active(AssetStatus.Retired), statistics.GetProperty("retiredCount").GetInt32());
        Assert.Equal(byStatus.Where(s => s.Archived).Sum(s => s.Count), statistics.GetProperty("archivedCount").GetInt32());
        Assert.Equal((20, 5, 2, 1, 2), (
            statistics.GetProperty("totalCount").GetInt32(),
            statistics.GetProperty("assignedCount").GetInt32(),
            statistics.GetProperty("faultyCount").GetInt32(),
            statistics.GetProperty("retiredCount").GetInt32(),
            statistics.GetProperty("archivedCount").GetInt32()));

        // Cities, departments and brands, with their assigned assets: the eight largest, and the rest together.
        var cities = await DistributionAsync("Cities", "CityId");
        var departments = await DistributionAsync("Departments", "DepartmentId");
        var brands = await DistributionAsync("Brands", "BrandId");
        AssertDistribution(cities, statistics.GetProperty("byCity"));
        AssertDistribution(departments, statistics.GetProperty("byDepartment"));
        AssertDistribution(brands, statistics.GetProperty("byBrand"));
        Assert.Equal((10, 9, 10), (cities.Count, departments.Count, brands.Count));
        Assert.Equal((2, 3, 1), (
            statistics.GetProperty("byCity").GetProperty("otherGroupCount").GetInt32(),
            statistics.GetProperty("byCity").GetProperty("otherCount").GetInt32(),
            statistics.GetProperty("byCity").GetProperty("otherAssignedCount").GetInt32()));
        // Both archived assets are of the second brand, which falls behind the third.
        Assert.Equal([("Marka 01", 6), ("Marka 03", 4), ("Marka 02", 3)], Items(statistics.GetProperty("byBrand")).Take(3).Select(b => (b.Name, b.Count)));
        Assert.Equal((2, 2), (statistics.GetProperty("byBrand").GetProperty("otherGroupCount").GetInt32(), statistics.GetProperty("byBrand").GetProperty("otherCount").GetInt32()));
        // Ties are broken in Turkish alphabetical order: Ç after C, İ after I, Ş after S.
        Assert.Equal(
            ["Ankara", "Bursa", "Çorum", "Iğdır", "İstanbul", "İzmir", "Ordu", "Şanlıurfa"],
            Items(statistics.GetProperty("byCity")).Select(c => c.Name));

        // Types, by name.
        var types = await QueryAsync(
            "SELECT AssetType, COUNT(*) FROM dbo.Assets WHERE IsDeleted = 0 GROUP BY AssetType ORDER BY COUNT(*) DESC, AssetType",
            r => (Enum.GetName((AssetType)r.GetInt32(0)), r.GetInt32(1)));
        Assert.Equal(
            types,
            statistics.GetProperty("byType").EnumerateArray().Select(t => (t.GetProperty("assetType").GetString(), t.GetProperty("count").GetInt32())));

        // Every distribution adds up to the total.
        var total = statistics.GetProperty("totalCount").GetInt32();
        Assert.All(
            DistributionNames,
            name => Assert.Equal(total, Items(statistics.GetProperty(name)).Sum(c => c.Count) + statistics.GetProperty(name).GetProperty("otherCount").GetInt32()));
        Assert.Equal(total, types.Sum(t => t.Item2));
    }

    [SqlServerFact]
    public async Task Monthly_assignments_and_returns_match_SQL_Servers_own_Istanbul_months()
    {
        using var client = _api!.CreateSignedInClient();

        var statistics = await client.GetOkAsync("/api/dashboard/statistics");

        var months = Movements(statistics);
        Assert.Equal(
            ["2025-11", "2025-12", "2026-01", "2026-02", "2026-03", "2026-04", "2026-05", "2026-06", "2026-07", "2026-08", "2026-09", "2026-10"],
            months.Select(m => m.Month));
        var assigned = await MonthCountsAsync("AssignedAt");
        var returned = await MonthCountsAsync("ReturnedAt");
        Assert.All(months, m =>
        {
            Assert.Equal(assigned.GetValueOrDefault(m.Month), m.Assigned);
            Assert.Equal(returned.GetValueOrDefault(m.Month), m.Returned);
        });

        // What those counts are, month by month: the boundaries fall on Istanbul midnights.
        Assert.Equal(
            [
                ("2025-11", 1, 1), ("2026-01", 1, 0), ("2026-02", 0, 1), ("2026-03", 1, 1), ("2026-04", 1, 0),
                ("2026-09", 1, 0), ("2026-10", 1, 1),
            ],
            months.Where(m => m.Assigned + m.Returned > 0).Select(m => (m.Month, m.Assigned, m.Returned)));
        Assert.Equal(2, assigned["2025-10"]);
    }

    [SqlServerFact]
    public async Task The_figures_follow_assignments_and_returns_made_through_the_API()
    {
        using var client = _api!.CreateSignedInClient("ayse.admin");
        var before = await client.GetOkAsync("/api/dashboard/statistics");
        var asset = (await client.GetOkAsync("/api/assets?search=T-12")).GetProperty("items")[0];
        var details = await client.GetOkAsync($"/api/assets/{asset.Id()}");

        var assigned = await client.AssignAsync(details, await client.EmployeeGuidAsync("dev.user"));
        var afterAssignment = await client.GetOkAsync("/api/dashboard/statistics");
        _clock.Advance(TimeSpan.FromHours(1));
        using (var giveBack = await client.PostReturnAsync(asset.Id(), assigned.GetProperty("rowVersion").GetString()))
        {
            Assert.Equal(HttpStatusCode.OK, giveBack.StatusCode);
        }

        var afterReturn = await client.GetOkAsync("/api/dashboard/statistics");

        Assert.Equal(before.GetProperty("assignedCount").GetInt32() + 1, afterAssignment.GetProperty("assignedCount").GetInt32());
        Assert.Equal(Movements(before)[^1].Assigned + 1, Movements(afterAssignment)[^1].Assigned);
        Assert.Equal(before.GetProperty("assignedCount").GetInt32(), afterReturn.GetProperty("assignedCount").GetInt32());
        Assert.Equal(Movements(before)[^1].Returned + 1, Movements(afterReturn)[^1].Returned);
        Assert.Equal((await MonthCountsAsync("AssignedAt"))["2026-10"], Movements(afterReturn)[^1].Assigned);
        Assert.Equal((await MonthCountsAsync("ReturnedAt"))["2026-10"], Movements(afterReturn)[^1].Returned);
    }

    /// <summary>Assets that are not archived per row of <paramref name="table"/>, largest first, then by name in Turkish order.</summary>
    private async Task<List<(int Id, string Name, int Count, int Assigned)>> DistributionAsync(string table, string column)
    {
        var rows = await QueryAsync(
            $"""
            SELECT t.Id, t.Name, COUNT(*), SUM(CASE WHEN a.Status = {(int)AssetStatus.Assigned} THEN 1 ELSE 0 END)
            FROM dbo.Assets a JOIN dbo.{table} t ON t.Id = a.{column}
            WHERE a.IsDeleted = 0
            GROUP BY t.Id, t.Name
            """,
            r => (r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3)));
        var turkish = StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: false);
        return [.. rows.OrderByDescending(r => r.Item3).ThenBy(r => r.Item2, turkish)];
    }

    /// <summary>Assignments or returns per month of Istanbul, by SQL Server (Turkey Standard Time is Istanbul).</summary>
    private async Task<Dictionary<string, int>> MonthCountsAsync(string column)
    {
        var rows = await QueryAsync(
            $"""
            SELECT FORMAT({column} AT TIME ZONE 'Turkey Standard Time', 'yyyy-MM'), COUNT(*)
            FROM dbo.AssetAssignments
            WHERE {column} IS NOT NULL
            GROUP BY FORMAT({column} AT TIME ZONE 'Turkey Standard Time', 'yyyy-MM')
            """,
            r => (r.GetString(0), r.GetInt32(1)));
        return rows.ToDictionary(r => r.Item1, r => r.Item2);
    }

    private async Task<List<T>> QueryAsync<T>(string sql, Func<SqlDataReader, T> read)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<T>();
        while (await reader.ReadAsync())
        {
            rows.Add(read(reader));
        }

        return rows;
    }

    /// <summary>The eight largest by name and count, and how many others there are with how many (assigned) assets.</summary>
    private static void AssertDistribution(List<(int Id, string Name, int Count, int Assigned)> expected, JsonElement distribution)
    {
        var rest = expected.Skip(8).ToList();
        Assert.Equal(expected.Take(8), Items(distribution));
        Assert.Equal(
            (rest.Count, rest.Sum(r => r.Count), rest.Sum(r => r.Assigned)),
            (distribution.GetProperty("otherGroupCount").GetInt32(), distribution.GetProperty("otherCount").GetInt32(), distribution.GetProperty("otherAssignedCount").GetInt32()));
    }

    private static List<(int Id, string Name, int Count, int Assigned)> Items(JsonElement distribution) =>
        [.. distribution.GetProperty("items").EnumerateArray().Select(i => (
            i.GetProperty("id").GetInt32(),
            i.GetProperty("name").GetString()!,
            i.GetProperty("count").GetInt32(),
            i.GetProperty("assignedCount").GetInt32()))];

    private static List<(string Month, int Assigned, int Returned)> Movements(JsonElement statistics) =>
        [.. statistics.GetProperty("monthlyMovements").EnumerateArray().Select(m => (
            m.GetProperty("month").GetString()!,
            m.GetProperty("assignedCount").GetInt32(),
            m.GetProperty("returnedCount").GetInt32()))];
}
