using System.Net;
using System.Text.Json;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Assets;
using EnterpriseInventory.IntegrationTests.Persistence;
using EnterpriseInventory.Tests.Exports;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseInventory.IntegrationTests.Reports;

/// <summary>
/// <c>GET /api/reports/assignments</c> and its Excel twin on a database of their own: the movements against plain SQL
/// that finds the days with SQL Server's own <c>AT TIME ZONE</c>, independently of the API's time zone code.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class AssignmentReportTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime, IDisposable
{
    private const string ReportPath = "/api/reports/assignments";

    private TestApiFactory? _api;
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        if (SqlServerDatabaseFixture.ServerConnectionString is null)
        {
            return;
        }

        _connectionString = await fixture.CreateMigratedDatabaseAsync();
        await ReportSeed.SeedAsync(fixture, _connectionString);
        _api = Api();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _api?.Dispose();

    private TestApiFactory Api(int maxExportRows = 50_000) => new(
        _connectionString,
        settings: new Dictionary<string, string?>(LoginEndpointTests.FakeDirectory)
        {
            ["RateLimiting:PermitLimit"] = "1000",
            ["Reporting:MaxExportRows"] = maxExportRows.ToString(System.Globalization.CultureInfo.InvariantCulture),
        },
        configureServices: services => services.AddSingleton<TimeProvider>(new TestClock(ReportSeed.Now)));

    [SqlServerFact]
    public async Task A_period_holds_the_movements_SQL_Server_puts_on_those_Istanbul_days()
    {
        using var client = _api!.CreateSignedInClient();

        var october = await client.GetOkAsync($"{ReportPath}?from=2026-10-01&to=2026-10-31&pageSize=100");

        Assert.Equal(await MovementsAsync("2026-10-01", "2026-10-31"), Movements(october));
        Assert.Equal(
            [
                ("RP-02", "Assigned", "2026-10-31T20:59:00+00:00"), // 23:59 on 31 October in Istanbul
                ("RP-04", "Returned", "2026-10-15T09:00:00+00:00"),
                ("RP-03", "Assigned", "2026-10-05T08:00:00+00:00"),
                ("RP-06", "Returned", "2026-10-03T07:00:00+00:00"),
                ("RP-06", "Assigned", "2026-10-02T07:00:00+00:00"),
                ("RP-01", "Assigned", "2026-09-30T21:30:00+00:00"), // 00:30 on 1 October in Istanbul
            ],
            Movements(october).Select(m => (m.AssetCode, m.Movement, m.At.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture))));
        Assert.Equal((6, 4, 2), Totals(october));

        // 23:30 on 30 September is September; the return at 00:00 on 1 November is November.
        var september = await client.GetOkAsync($"{ReportPath}?from=2026-09-30&to=2026-09-30");
        Assert.Equal(["RP-00"], Movements(september).Select(m => m.AssetCode));
        var november = await client.GetOkAsync($"{ReportPath}?from=2026-11-01");
        Assert.Equal([("RP-03", "Returned")], Movements(november).Select(m => (m.AssetCode, m.Movement)));
    }

    [SqlServerFact]
    public async Task Without_a_period_every_movement_is_listed_newest_first_and_the_pages_do_not_overlap()
    {
        using var client = _api!.CreateSignedInClient();

        var all = await client.GetOkAsync($"{ReportPath}?pageSize=100");
        var pages = new List<(string, string, DateTimeOffset)>();
        for (var page = 1; page <= 5; page++)
        {
            pages.AddRange(Movements(await client.GetOkAsync($"{ReportPath}?page={page}&pageSize=2")).Select(m => (m.AssetCode, m.Movement, m.At)));
        }

        Assert.Equal(await MovementsAsync(null, null), Movements(all));
        Assert.Equal((9, 6, 3), Totals(all));
        Assert.Equal(Movements(all).Select(m => (m.AssetCode, m.Movement, m.At)), pages);
    }

    [SqlServerFact]
    public async Task Movements_of_archived_assets_are_listed_and_marked()
    {
        using var client = _api!.CreateSignedInClient();

        var report = await client.GetOkAsync($"{ReportPath}?search=RP-06");

        var items = report.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.True(i.GetProperty("assetArchived").GetBoolean()));
        Assert.Equal("admin.bir", items[0].GetProperty("by").GetString());
        Assert.Equal("mehmet.kaya", items[0].GetProperty("employeeUserName").GetString());
        Assert.Equal("Mehmet Kaya", items[0].GetProperty("employeeDisplayName").GetString());
    }

    [SqlServerFact]
    public async Task The_filters_narrow_the_movements()
    {
        using var client = _api!.CreateSignedInClient();

        var returns = await client.GetOkAsync($"{ReportPath}?movement=Returned");
        var byEmployee = await client.GetOkAsync($"{ReportPath}?search=ayse");
        var byAccent = await client.GetOkAsync($"{ReportPath}?search=AYŞE yilmaz");
        var laptops = await client.GetOkAsync($"{ReportPath}?assetType=Laptop");
        var cities = await client.GetOkAsync("/api/reports/asset-summary?groupBy=city");
        var ankara = cities.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("name").GetString() == "Ankara").GetProperty("key").GetString();
        var inAnkara = await client.GetOkAsync($"{ReportPath}?cityId={ankara}");

        Assert.Equal((3, 0, 3), Totals(returns));
        Assert.All(Movements(returns), m => Assert.Equal("Returned", m.Movement));
        Assert.Equal(["RP-02", "RP-04", "RP-00", "RP-04"], Movements(byEmployee).Select(m => m.AssetCode));
        Assert.Equal(Movements(byEmployee), Movements(byAccent));
        Assert.Equal(["RP-03", "RP-03", "RP-00"], Movements(laptops).Select(m => m.AssetCode));

        // RP-01, RP-04 and RP-07 are in Ankara; RP-07 was never assigned.
        Assert.Equal(["RP-04", "RP-01", "RP-04"], Movements(inAnkara).Select(m => m.AssetCode));
    }

    [SqlServerFact]
    public async Task Invalid_days_and_filters_are_refused_with_Turkish_messages()
    {
        using var client = _api!.CreateSignedInClient();

        async Task<JsonElement> Refused(string query)
        {
            using var response = await client.GetAsync(AssetApi.Uri($"{ReportPath}?{query}"));
            return await AssetApi.ReadAsync(response, HttpStatusCode.BadRequest);
        }

        Assert.StartsWith("Başlangıç tarihi geçersiz.", (await Refused("from=2026-13-01")).FieldError("from"), StringComparison.Ordinal);
        Assert.StartsWith("Başlangıç tarihi geçersiz.", (await Refused("from=01.10.2026")).FieldError("from"), StringComparison.Ordinal);
        Assert.StartsWith("Bitiş tarihi geçersiz.", (await Refused("to=9999-12-31")).FieldError("to"), StringComparison.Ordinal);
        Assert.Equal("Bitiş tarihi başlangıç tarihinden önce olamaz.", (await Refused("from=2026-10-02&to=2026-10-01")).FieldError("to"));
        Assert.Equal("Hareket filtresi geçersiz. Geçerli değerler: Assigned, Returned.", (await Refused("movement=Lost")).FieldError("movement"));
        Assert.Equal("Arama en fazla 5 kelime içerebilir.", (await Refused("search=a b c d e f")).FieldError("search"));
        Assert.Equal("Departman filtresi geçersiz.", (await Refused("departmentId=-1")).FieldError("departmentId"));

        // One day is a valid period.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(AssetApi.Uri($"{ReportPath}?from=2026-10-01&to=2026-10-01"))).StatusCode);
    }

    [SqlServerFact]
    public async Task The_Excel_file_holds_every_movement_in_Istanbul_time_and_what_was_asked()
    {
        using var client = _api!.CreateSignedInClient("ayse.admin");
        const string Query = "from=2026-10-01&to=2026-10-31";
        var october = await client.GetOkAsync($"{ReportPath}?{Query}&pageSize=2");

        using var response = await client.GetAsync(AssetApi.Uri($"{ReportPath}/export?{Query}&page=3&pageSize=1"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("zimmet-hareketleri-2026-11-10.xlsx", response.Content.Headers.ContentDisposition?.FileNameStar);
        var file = SpreadsheetFile.Read(await response.Content.ReadAsByteArrayAsync());
        Assert.Empty(file.SchemaErrors);
        var sheet = file["Hareketler"];
        Assert.Equal(
            ["Tarih", "Hareket", "Demirbaş Kodu", "Tür", "Marka", "Model", "Seri No", "Kullanıcı Adı", "Ad Soyad", "Zimmet Tanımı", "Şehir", "Departman", "İşlemi Yapan", "Arşivlenmiş"],
            sheet.Header);

        // Every page of it, whatever page was asked for.
        Assert.Equal(6, sheet.DataRows.Count);
        var first = sheet.DataRows[0];
        Assert.Equal(new DateTime(2026, 10, 31, 23, 59, 0), first[0]!.AsDateTime());
        Assert.Equal(["Zimmet verildi", "RP-02", "Monitör", "HP", "EliteBook 840"], first.Skip(1).Take(5).Select(c => c!.Text));
        Assert.Equal(["ayse.yilmaz", "Ayşe Yılmaz"], first.Skip(7).Take(2).Select(c => c!.Text));
        Assert.Equal(["İzmir", "Muhasebe", "admin.bir"], first.Skip(10).Take(3).Select(c => c!.Text));
        Assert.Null(first.ElementAtOrDefault(13));
        Assert.Equal(new DateTime(2026, 10, 1, 0, 30, 0), sheet.DataRows[5][0]!.AsDateTime());
        Assert.Equal(["İade alındı", "Evet"], [sheet.DataRows[3][1]!.Text, sheet.DataRows[3][13]!.Text]);
        Assert.Equal(
            Movements(await client.GetOkAsync($"{ReportPath}?{Query}&pageSize=100")).Select(m => m.AssetCode),
            sheet.DataRows.Select(r => r[2]!.Text));

        var info = file["Bilgi"];
        Assert.Equal("Zimmet hareketleri", info.Value("Rapor")!.Text);
        Assert.Equal("ayse.admin", info.Value("Oluşturan")!.Text);
        Assert.Equal("01.10.2026", info.Value("Başlangıç tarihi")!.Text);
        Assert.Equal("31.10.2026", info.Value("Bitiş tarihi")!.Text);
        Assert.Equal("Tümü", info.Value("Hareket")!.Text);
        Assert.Equal(Totals(october).Assigned, info.Value("Zimmet verildi")!.AsInt());
        Assert.Equal(Totals(october).Returned, info.Value("İade alındı")!.AsInt());
        Assert.Equal("Şehir ve departman, demirbaşın bugünkü yeridir.", info.Value("Not")!.Text);
    }

    [SqlServerFact]
    public async Task An_export_larger_than_the_limit_is_refused_in_Turkish_before_anything_is_read()
    {
        using var api = Api(maxExportRows: 5);
        using var client = api.CreateSignedInClient();

        using var tooMany = await client.GetAsync(AssetApi.Uri($"{ReportPath}/export"));
        using var fits = await client.GetAsync(AssetApi.Uri($"{ReportPath}/export?movement=Returned"));

        var problem = await AssetApi.ReadAsync(tooMany, HttpStatusCode.BadRequest);
        Assert.Equal("export_too_large", problem.GetProperty("code").GetString());
        Assert.Equal("Aktarılacak hareket sayısı sınırı aşıyor.", problem.GetProperty("title").GetString());
        Assert.Equal(
            "Filtrelerle eşleşen 9 hareket var; bir dosyaya en fazla 5 hareket aktarılabilir. Tarih aralığını veya filtreleri daraltıp tekrar deneyin.",
            problem.GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.OK, fits.StatusCode);
    }

    /// <summary>The movements in SQL: assignments by AssignedAt and returns by ReturnedAt, days found by SQL Server in Istanbul time.</summary>
    private async Task<List<Row>> MovementsAsync(string? from, string? to)
    {
        static string Period(string column, string? from, string? to) =>
            $"{(from is null ? string.Empty : $" AND CAST({column} AT TIME ZONE 'Turkey Standard Time' AS date) >= '{from}'")}"
            + $"{(to is null ? string.Empty : $" AND CAST({column} AT TIME ZONE 'Turkey Standard Time' AS date) <= '{to}'")}";

        return await QueryAsync(
            $"""
            SELECT a.AssetCode, m.Kind, m.At, m.Id
            FROM (
                SELECT x.Id, x.AssetId, 0 AS Kind, x.AssignedAt AS At FROM dbo.AssetAssignments x WHERE 1 = 1{Period("x.AssignedAt", from, to)}
                UNION ALL
                SELECT x.Id, x.AssetId, 1, x.ReturnedAt FROM dbo.AssetAssignments x WHERE x.ReturnedAt IS NOT NULL{Period("x.ReturnedAt", from, to)}
            ) m
            JOIN dbo.Assets a ON a.Id = m.AssetId
            ORDER BY m.At DESC, m.Kind DESC, m.Id DESC
            """,
            r => new Row(r.GetString(0), r.GetInt32(1) == 0 ? "Assigned" : "Returned", r.GetDateTimeOffset(2), r.GetInt32(3)));
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

    private static List<Row> Movements(JsonElement report) =>
        [.. report.GetProperty("items").EnumerateArray().Select(m => new Row(
            m.GetProperty("assetCode").GetString()!,
            m.GetProperty("movement").GetString()!,
            m.GetProperty("at").GetDateTimeOffset(),
            m.GetProperty("assignmentId").GetInt32()))];

    private static (int Total, int Assigned, int Returned) Totals(JsonElement report) => (
        report.GetProperty("totalCount").GetInt32(),
        report.GetProperty("assignedCount").GetInt32(),
        report.GetProperty("returnedCount").GetInt32());

    private sealed record Row(string AssetCode, string Movement, DateTimeOffset At, int AssignmentId);
}
