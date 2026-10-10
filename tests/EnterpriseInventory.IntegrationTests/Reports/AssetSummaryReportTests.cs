using System.Globalization;
using System.Net;
using System.Text.Json;
using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Assets;
using EnterpriseInventory.IntegrationTests.Persistence;
using EnterpriseInventory.Tests.Exports;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseInventory.IntegrationTests.Reports;

/// <summary>
/// <c>GET /api/reports/asset-summary</c> and its Excel twin on a database of their own: every grouping's rows against
/// the same counts made with plain SQL, and every row's inventory link against what the inventory list counts.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class AssetSummaryReportTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime, IDisposable
{
    private const string SummaryPath = "/api/reports/asset-summary";

    private static readonly Dictionary<int, string> TypeLabels = new()
    {
        [(int)AssetType.Laptop] = "Dizüstü",
        [(int)AssetType.Desktop] = "Masaüstü",
        [(int)AssetType.Monitor] = "Monitör",
        [(int)AssetType.Printer] = "Yazıcı",
    };

    private static readonly Dictionary<int, string> StatusLabels = new()
    {
        [(int)AssetStatus.Available] = "Boşta",
        [(int)AssetStatus.Assigned] = "Zimmetli",
        [(int)AssetStatus.Faulty] = "Arızalı",
        [(int)AssetStatus.Retired] = "Hurda",
    };

    private TestApiFactory? _api;
    private string _connectionString = string.Empty;

    public static TheoryData<string> Groupings => ["city", "department", "location", "brand", "model", "assetType", "status"];

    public async Task InitializeAsync()
    {
        if (SqlServerDatabaseFixture.ServerConnectionString is null)
        {
            return;
        }

        _connectionString = await fixture.CreateMigratedDatabaseAsync();
        await ReportSeed.SeedAsync(fixture, _connectionString);
        _api = new TestApiFactory(
            _connectionString,
            settings: new Dictionary<string, string?>(LoginEndpointTests.FakeDirectory) { ["RateLimiting:PermitLimit"] = "1000" },
            configureServices: services => services.AddSingleton<TimeProvider>(new TestClock(ReportSeed.Now)));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _api?.Dispose();

    [SqlServerTheory]
    [MemberData(nameof(Groupings))]
    public async Task Every_grouping_matches_the_database_counted_with_plain_SQL(string groupBy)
    {
        using var client = _api!.CreateSignedInClient();

        var summary = await client.GetOkAsync($"{SummaryPath}?groupBy={groupBy}");

        var expected = await CountAsync(groupBy);
        Assert.Equal(expected, Rows(summary).Select(r => (r.Name, r.Counts)));
        var total = summary.GetProperty("total");
        Assert.Equal("Toplam", total.GetProperty("name").GetString());
        Assert.Equal(
            (expected.Sum(e => e.Counts.Total), expected.Sum(e => e.Counts.Assigned), expected.Sum(e => e.Counts.Available), expected.Sum(e => e.Counts.Faulty), expected.Sum(e => e.Counts.Retired)),
            Counts(total));

        // 14 assets that are not archived, whatever they are grouped by.
        Assert.Equal(14, Counts(total).Total);
    }

    [SqlServerFact]
    public async Task The_groups_are_named_in_Turkish_largest_first_in_Turkish_alphabetical_order()
    {
        using var client = _api!.CreateSignedInClient();

        var cities = await client.GetOkAsync(SummaryPath);
        var locations = await client.GetOkAsync($"{SummaryPath}?groupBy=location");
        var models = await client.GetOkAsync($"{SummaryPath}?groupBy=MODEL");
        var statuses = await client.GetOkAsync($"{SummaryPath}?groupBy=status");

        Assert.Equal("City", cities.GetProperty("groupBy").GetString());
        // İstanbul and İzmir have five assets each (İs before İz), Ankara four.
        Assert.Equal(["İstanbul", "İzmir", "Ankara"], Rows(cities).Select(r => r.Name));
        Assert.Equal(
            ["Lokasyon belirtilmemiş", "Çankaya Ofis (Ankara)", "Merkez Ofis (İstanbul)", "Ümraniye Depo (İstanbul)"],
            Rows(locations).Select(r => r.Name));
        Assert.Contains("Dell Latitude 5440", Rows(models).Select(r => r.Name));
        Assert.Equal(["Boşta", "Zimmetli", "Arızalı", "Hurda"], Rows(statuses).Select(r => r.Name));
        Assert.Equal(["Available", "Assigned", "Faulty", "Retired"], statuses.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("key").GetString()));
    }

    [SqlServerTheory]
    [MemberData(nameof(Groupings))]
    public async Task Every_row_links_to_an_inventory_list_of_the_same_assets(string groupBy)
    {
        using var client = _api!.CreateSignedInClient();
        (string Name, string Value)[] filters = [("assetType", "Laptop"), ("assetType", "Desktop"), ("status", "Available"), ("status", "Assigned")];
        static string Query(IEnumerable<(string Name, string Value)> values) => string.Join('&', values.Select(v => $"{v.Name}={v.Value}"));

        var summary = await client.GetOkAsync($"{SummaryPath}?groupBy={groupBy}&{Query(filters)}");

        var list = await client.GetOkAsync($"/api/assets?{Query(filters)}&pageSize=1");
        Assert.Equal(list.GetProperty("totalCount").GetInt32(), Counts(summary.GetProperty("total")).Total);
        foreach (var row in summary.GetProperty("rows").EnumerateArray())
        {
            if (row.GetProperty("inventoryFilter").ValueKind == JsonValueKind.Null)
            {
                Assert.Equal("none", row.GetProperty("key").GetString());
                continue;
            }

            // The row's filter takes the place of the report's filter of the same name (a type row: that type only).
            var rowFilter = row.GetProperty("inventoryFilter").EnumerateObject().Select(p => (p.Name, p.Value.GetString()!)).ToList();
            var link = filters.Where(f => rowFilter.All(r => r.Name != f.Name)).Concat(rowFilter);
            var assets = await client.GetOkAsync($"/api/assets?{Query(link)}&pageSize=1");
            Assert.Equal(row.GetProperty("totalCount").GetInt32(), assets.GetProperty("totalCount").GetInt32());
        }
    }

    [SqlServerFact]
    public async Task The_inventory_filters_apply_as_they_do_to_the_list()
    {
        using var client = _api!.CreateSignedInClient();

        foreach (var query in new[] { "status=Assigned", "search=latitude", "search=istanbul ofis", "search=ankara&status=Faulty&status=Retired" })
        {
            var summary = await client.GetOkAsync($"{SummaryPath}?groupBy=department&{query}");
            var list = await client.GetOkAsync($"/api/assets?{query}&pageSize=1");
            Assert.Equal(list.GetProperty("totalCount").GetInt32(), Counts(summary.GetProperty("total")).Total);
        }

        var assigned = await client.GetOkAsync($"{SummaryPath}?status=Assigned");
        Assert.Equal((3, 3, 0, 0, 0), Counts(assigned.GetProperty("total")));
    }

    [SqlServerFact]
    public async Task Invalid_groupings_and_filters_are_refused_with_Turkish_messages()
    {
        using var client = _api!.CreateSignedInClient();

        using var grouping = await client.GetAsync(AssetApi.Uri($"{SummaryPath}?groupBy=sehir"));
        using var status = await client.GetAsync(AssetApi.Uri($"{SummaryPath}?status=Kayip"));
        using var number = await client.GetAsync(AssetApi.Uri($"{SummaryPath}?groupBy=1"));
        using var export = await client.GetAsync(AssetApi.Uri($"{SummaryPath}/export?cityId=0"));

        Assert.Equal(
            "Gruplama geçersiz. Geçerli değerler: city, department, location, brand, model, assetType, status.",
            (await AssetApi.ReadAsync(grouping, HttpStatusCode.BadRequest)).FieldError("groupBy"));
        Assert.StartsWith("Durum filtresi geçersiz.", (await AssetApi.ReadAsync(status, HttpStatusCode.BadRequest)).FieldError("status"), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, number.StatusCode);
        Assert.Equal("Şehir filtresi geçersiz.", (await AssetApi.ReadAsync(export, HttpStatusCode.BadRequest)).FieldError("cityId"));
    }

    [SqlServerFact]
    public async Task The_Excel_file_holds_the_same_rows_a_total_and_what_was_asked()
    {
        using var client = _api!.CreateSignedInClient("ayse.admin");
        var summary = await client.GetOkAsync($"{SummaryPath}?groupBy=location&status=Available&status=Faulty");

        using var response = await client.GetAsync(AssetApi.Uri($"{SummaryPath}/export?groupBy=location&status=Available&status=Faulty"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("envanter-ozeti-lokasyon-2026-11-10.xlsx", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var file = SpreadsheetFile.Read(await response.Content.ReadAsByteArrayAsync());
        Assert.Empty(file.SchemaErrors);

        var sheet = file["Özet"];
        Assert.Equal(["Lokasyon", "Toplam", "Zimmetli", "Boşta", "Arızalı", "Hurda"], sheet.Header);
        var expected = Rows(summary).Select(r => (r.Name, r.Counts)).Append(("Toplam", Counts(summary.GetProperty("total")))).ToList();
        Assert.Equal(
            expected,
            sheet.DataRows.Select(r => (r[0]!.Text, (r[1]!.AsInt(), r[2]!.AsInt(), r[3]!.AsInt(), r[4]!.AsInt(), r[5]!.AsInt()))));
        Assert.All(sheet.DataRows, r => Assert.Equal("n", r[1]!.Type));

        var info = file["Bilgi"];
        Assert.Equal("Envanter özeti", info.Value("Rapor")!.Text);
        Assert.Equal("Lokasyon", info.Value("Gruplama")!.Text);
        Assert.Equal("Arşivlenmemiş demirbaşlar", info.Value("Kapsam")!.Text);
        Assert.Equal("ayse.admin", info.Value("Oluşturan")!.Text);
        Assert.Equal(new DateTime(2026, 11, 10, 9, 0, 0), info.Value("Oluşturulma zamanı")!.AsDateTime());
        Assert.Equal("Europe/Istanbul (UTC+03:00)", info.Value("Saat dilimi")!.Text);
        Assert.Equal(Counts(summary.GetProperty("total")).Total, info.Value("Demirbaş sayısı")!.AsInt());
        Assert.Equal(Rows(summary).Count, info.Value("Lokasyon sayısı")!.AsInt());
        Assert.Equal("Boşta, Arızalı", info.Value("Durum")!.Text);
        Assert.Equal("Tümü", info.Value("Şehir")!.Text);
    }

    [Fact]
    public async Task Visitors_get_401_and_users_without_the_administrator_role_get_403()
    {
        await using var api = new TestApiFactory();
        using var visitor = api.CreateAnonymousClient();
        using var reader = api.CreateSignedInClient("veli.user", roles: "Reader");

        foreach (var path in new[] { SummaryPath, $"{SummaryPath}/export", "/api/reports/assignments", "/api/reports/assignments/export" })
        {
            using var anonymous = await visitor.GetAsync(AssetApi.Uri(path));
            using var forbidden = await reader.GetAsync(AssetApi.Uri(path));

            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
    }

    /// <summary>Assets that are not archived per group, counted by status in SQL; largest first, then Turkish alphabetical order.</summary>
    private async Task<List<(string Name, (int Total, int Assigned, int Available, int Faulty, int Retired) Counts)>> CountAsync(string groupBy)
    {
        var (select, from, groupKey) = groupBy switch
        {
            "city" => ("g.Name", "JOIN dbo.Cities g ON g.Id = a.CityId", "g.Id, g.Name"),
            "department" => ("g.Name", "JOIN dbo.Departments g ON g.Id = a.DepartmentId", "g.Id, g.Name"),
            "location" => (
                "CASE WHEN g.Id IS NULL THEN N'Lokasyon belirtilmemiş' ELSE g.Name + N' (' + p.Name + N')' END",
                "LEFT JOIN dbo.Locations g ON g.Id = a.LocationId LEFT JOIN dbo.Cities p ON p.Id = g.CityId",
                "g.Id, g.Name, p.Name"),
            "brand" => ("g.Name", "JOIN dbo.Brands g ON g.Id = a.BrandId", "g.Id, g.Name"),
            "model" => ("p.Name + N' ' + g.Name", "JOIN dbo.AssetModels g ON g.Id = a.ModelId JOIN dbo.Brands p ON p.Id = g.BrandId", "g.Id, g.Name, p.Name"),
            "assetType" => ("CAST(a.AssetType AS nvarchar(10))", string.Empty, "a.AssetType"),
            _ => ("CAST(a.Status AS nvarchar(10))", string.Empty, "a.Status"),
        };
        string Sum(AssetStatus status) => $"SUM(CASE WHEN a.Status = {(int)status} THEN 1 ELSE 0 END)";

        var rows = await QueryAsync(
            $"""
            SELECT {select}, COUNT(*), {Sum(AssetStatus.Assigned)}, {Sum(AssetStatus.Available)}, {Sum(AssetStatus.Faulty)}, {Sum(AssetStatus.Retired)}
            FROM dbo.Assets a {from}
            WHERE a.IsDeleted = 0
            GROUP BY {groupKey}
            """,
            r => (Name: r.GetString(0), Counts: (r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5))));
        var named = rows.ConvertAll(r => groupBy switch
        {
            "assetType" => (TypeLabels[int.Parse(r.Name, CultureInfo.InvariantCulture)], r.Counts),
            "status" => (StatusLabels[int.Parse(r.Name, CultureInfo.InvariantCulture)], r.Counts),
            _ => r,
        });
        var turkish = StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: false);
        return [.. named.OrderByDescending(r => r.Counts.Item1).ThenBy(r => r.Item1, turkish)];
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

    private static List<(string Name, (int Total, int Assigned, int Available, int Faulty, int Retired) Counts)> Rows(JsonElement summary) =>
        [.. summary.GetProperty("rows").EnumerateArray().Select(r => (r.GetProperty("name").GetString()!, Counts(r)))];

    private static (int Total, int Assigned, int Available, int Faulty, int Retired) Counts(JsonElement row) => (
        row.GetProperty("totalCount").GetInt32(),
        row.GetProperty("assignedCount").GetInt32(),
        row.GetProperty("availableCount").GetInt32(),
        row.GetProperty("faultyCount").GetInt32(),
        row.GetProperty("retiredCount").GetInt32());
}
