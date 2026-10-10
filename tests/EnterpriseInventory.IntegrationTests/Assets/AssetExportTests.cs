using System.Net;
using System.Text.Json;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Persistence;
using EnterpriseInventory.Tests.Exports;

namespace EnterpriseInventory.IntegrationTests.Assets;

/// <summary>
/// <c>GET /api/assets/export</c> on a database of its own: the file read back cell by cell holds exactly what
/// <c>GET /api/assets</c> lists for the same filters and order, every page of it.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class AssetExportTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime, IDisposable
{
    private const string ExportPath = "/api/assets/export";

    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    private static readonly Dictionary<string, string> TypeLabels = new()
    {
        ["Desktop"] = "Masaüstü",
        ["Laptop"] = "Dizüstü",
        ["Monitor"] = "Monitör",
    };

    private static readonly Dictionary<string, string> StatusLabels = new()
    {
        ["Available"] = "Boşta",
        ["Assigned"] = "Zimmetli",
        ["Faulty"] = "Arızalı",
        ["Retired"] = "Hurda",
    };

    private TestApiFactory? _api;
    private string _connectionString = string.Empty;
    private InventoryReferences? _references;

    public async Task InitializeAsync()
    {
        if (SqlServerDatabaseFixture.ServerConnectionString is null)
        {
            return;
        }

        _connectionString = await fixture.CreateMigratedDatabaseAsync();
        _references = await InventoryReferences.SeedAsync(fixture, _connectionString);
        _api = Api();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _api?.Dispose();

    private TestApiFactory Api(int? maxExportRows = null)
    {
        var settings = new Dictionary<string, string?>(LoginEndpointTests.FakeDirectory) { ["RateLimiting:PermitLimit"] = "1000" };
        if (maxExportRows is { } max)
        {
            settings["Reporting:MaxExportRows"] = max.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return new TestApiFactory(_connectionString, settings: settings);
    }

    /// <summary>
    /// Four assets that differ in every exported column: one assigned (with a formula-like description), one faulty
    /// without a computer name or serial number, one in another city, and one archived.
    /// </summary>
    private async Task SeedAsync(HttpClient client)
    {
        var references = _references!;
        var zeta = references.NewAssetBody("EX-001");
        zeta["computerName"] = "PC-ZETA";
        await client.CreateAssetAsync(zeta);

        var alfa = references.NewAssetBody("EX-002");
        alfa["assetType"] = "Desktop";
        alfa["computerName"] = "pc-alfa";
        alfa["cityId"] = references.SecondCityId;
        alfa["locationId"] = references.SecondCityLocationId;
        alfa["departmentId"] = references.SecondDepartmentId;
        var assigned = await client.CreateAssetAsync(alfa);
        await client.AssignAsync(assigned, await client.EmployeeGuidAsync("dev.user"), "=SUM(A1:A9) kasası");

        var faulty = references.NewAssetBody("EX-003");
        faulty["assetType"] = "Monitor";
        faulty["status"] = "Faulty";
        faulty["computerName"] = null;
        faulty["serialNumber"] = null;
        faulty["locationId"] = null;
        await client.CreateAssetAsync(faulty);

        var archived = await client.CreateAssetAsync(references.NewAssetBody("EX-004"));
        using var archive = await client.SendWithCsrfAsync(
            HttpMethod.Delete, $"/api/assets/{archived.Id()}?rowVersion={Uri.EscapeDataString(archived.GetProperty("rowVersion").GetString()!)}");
        Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
    }

    private static async Task<SpreadsheetFile> ExportAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync(AssetApi.Uri($"{ExportPath}?{query}"));
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected 200, got {(int)response.StatusCode}: {System.Text.Encoding.UTF8.GetString(bytes)}");
        return SpreadsheetFile.Read(bytes);
    }

    [SqlServerTheory]
    [InlineData("")]
    [InlineData("sortBy=computerName&sortDirection=desc")]
    [InlineData("sortBy=assignedDisplayName&sortDirection=desc")]
    [InlineData("status=Available&status=Faulty")]
    [InlineData("assetType=Monitor&assetType=Desktop&sortBy=assetType")]
    [InlineData("search=PC-ALF")]
    [InlineData("search=yok-boyle-bir-sey")]
    [InlineData("archived=true")]
    public async Task The_file_holds_exactly_what_the_list_shows_for_the_same_filters_and_order(string query)
    {
        using var client = _api!.CreateSignedInClient("ayse.admin");
        await SeedAsync(client);

        var list = await client.GetOkAsync($"/api/assets?{query}&pageSize=100");
        // The page the screen is on does not matter: the file holds every page.
        var file = await ExportAsync(client, $"{query}&page=2&pageSize=1");

        var sheet = file["Envanter"];
        var items = list.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(list.GetProperty("totalCount").GetInt32(), sheet.DataRows.Count);
        for (var i = 0; i < items.Count; i++)
        {
            AssertRow(items[i], sheet.DataRows[i]);
        }

        Assert.Empty(file.SchemaErrors);
    }

    [SqlServerFact]
    public async Task Every_page_is_exported_not_only_the_first()
    {
        using var client = _api!.CreateSignedInClient("ayse.admin");
        for (var i = 1; i <= 30; i++)
        {
            await client.CreateAssetAsync(_references!.NewAssetBody($"SAYFA-{i:D3}"));
        }

        var list = await client.GetOkAsync("/api/assets?search=SAYFA&sortBy=assetCode&sortDirection=desc");
        var file = await ExportAsync(client, "search=SAYFA&sortBy=assetCode&sortDirection=desc");

        Assert.Equal(25, list.GetProperty("items").GetArrayLength());
        Assert.Equal(30, list.GetProperty("totalCount").GetInt32());
        var codes = file["Envanter"].DataRows.Select(r => r[0]!.Text).ToList();
        Assert.Equal(Enumerable.Range(1, 30).Reverse().Select(i => $"SAYFA-{i:D3}"), codes);
        Assert.Equal(["Demirbaş sayısı", "30"], InfoRow(file, "Demirbaş sayısı"));
    }

    [SqlServerFact]
    public async Task The_answer_is_an_xlsx_attachment_named_for_the_day_and_never_cached()
    {
        using var client = _api!.CreateSignedInClient("ayse.admin");

        using var response = await client.GetAsync(AssetApi.Uri(ExportPath));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
        var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Istanbul).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal($"envanter-{today}.xlsx", response.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());

        using var archived = await client.GetAsync(AssetApi.Uri($"{ExportPath}?archived=true"));
        Assert.Equal($"envanter-arsiv-{today}.xlsx", archived.Content.Headers.ContentDisposition?.FileName);
    }

    [SqlServerFact]
    public async Task The_info_sheet_says_who_exported_what_when_and_with_which_filters()
    {
        using var ayse = _api!.CreateSignedInClient("ayse.admin");
        await SeedAsync(ayse);
        using var mehmet = _api.CreateSignedInClient("mehmet.admin");
        var before = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Istanbul).DateTime;

        var file = await ExportAsync(mehmet, $"cityId={_references!.SecondCityId}&status=Assigned&search=pc&sortBy=status&sortDirection=desc");

        var after = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Istanbul).DateTime;
        var row = Assert.Single(file["Envanter"].DataRows);
        Assert.Equal("EX-002", row[0]!.Text);
        Assert.Equal(["Rapor", "Envanter listesi"], InfoRow(file, "Rapor"));
        Assert.Equal(["Oluşturan", "mehmet.admin"], InfoRow(file, "Oluşturan"));
        var madeAt = file["Bilgi"].Value("Oluşturulma zamanı")!.AsDateTime();
        Assert.InRange(madeAt, before.AddSeconds(-1), after.AddSeconds(1));
        Assert.StartsWith("Europe/Istanbul (UTC+03:00)", file["Bilgi"].Value("Saat dilimi")!.Text, StringComparison.Ordinal);
        Assert.Equal(["Demirbaş sayısı", "1"], InfoRow(file, "Demirbaş sayısı"));
        Assert.Equal(["Arama", "pc"], InfoRow(file, "Arama"));
        Assert.Equal(["Durum", "Zimmetli"], InfoRow(file, "Durum"));
        Assert.Equal(["Tür", "Tümü"], InfoRow(file, "Tür"));
        Assert.Equal(["Şehir", row[8]!.Text], InfoRow(file, "Şehir"));
        Assert.Equal(["Marka", "Tümü"], InfoRow(file, "Marka"));
        Assert.Equal(["Sıralama", "Durum, azalan"], InfoRow(file, "Sıralama"));
    }

    [SqlServerFact]
    public async Task Text_that_looks_like_a_formula_is_written_as_text()
    {
        using var client = _api!.CreateSignedInClient("ayse.admin");
        await SeedAsync(client);

        var file = await ExportAsync(client, "search=EX-002");

        var description = Assert.Single(file["Envanter"].DataRows)[7]!;
        Assert.Equal("=SUM(A1:A9) kasası", description.Text);
        Assert.Equal("inlineStr", description.Type);
        Assert.False(description.HasFormula);
    }

    [SqlServerFact]
    public async Task More_assets_than_a_file_may_hold_are_refused_with_a_Turkish_message()
    {
        using var api = Api(maxExportRows: 2);
        using var client = api.CreateSignedInClient("ayse.admin");
        await SeedAsync(client);

        using var response = await client.GetAsync(AssetApi.Uri(ExportPath));

        var problem = await AssetApi.ReadAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal("export_too_large", problem.GetProperty("code").GetString());
        Assert.Equal("Aktarılacak demirbaş sayısı sınırı aşıyor.", problem.GetProperty("title").GetString());
        Assert.Equal(
            "Filtrelerle eşleşen 3 demirbaş var; bir dosyaya en fazla 2 demirbaş aktarılabilir. Filtreleri daraltıp tekrar deneyin.",
            problem.GetProperty("detail").GetString());

        // Narrower filters fit.
        var file = await ExportAsync(client, "status=Faulty");
        Assert.Equal("EX-003", Assert.Single(file["Envanter"].DataRows)[0]!.Text);
    }

    [SqlServerTheory]
    [InlineData("status=Bozuk", "status")]
    [InlineData("assetType=1", "assetType")]
    [InlineData("sortBy=fiyat", "sortBy")]
    [InlineData("cityId=0", "cityId")]
    [InlineData("search=%01", "search")]
    public async Task Filters_the_list_refuses_are_refused_the_same_way(string query, string field)
    {
        using var client = _api!.CreateSignedInClient("ayse.admin");

        using var list = await client.GetAsync(AssetApi.Uri($"/api/assets?{query}"));
        using var export = await client.GetAsync(AssetApi.Uri($"{ExportPath}?{query}"));

        var listProblem = await AssetApi.ReadAsync(list, HttpStatusCode.BadRequest);
        var exportProblem = await AssetApi.ReadAsync(export, HttpStatusCode.BadRequest);
        Assert.Equal(listProblem.FieldError(field), exportProblem.FieldError(field));
    }

    /// <summary>One exported row against the list item it should repeat, column by column.</summary>
    private static void AssertRow(JsonElement item, IReadOnlyList<SpreadsheetFile.CellContent?> row)
    {
        string? Text(string property) => item.GetProperty(property).ValueKind == JsonValueKind.Null ? null : item.GetProperty(property).GetString();

        string?[] expected =
        [
            Text("assetCode"),
            Text("assignedUserName"),
            Text("assignedDisplayName"),
            Text("computerName"),
            Text("brandName"),
            Text("modelName"),
            Text("serialNumber"),
            Text("assignmentDescription"),
            Text("cityName"),
            Text("locationName"),
            Text("departmentName"),
            TypeLabels[Text("assetType")!],
            StatusLabels[Text("status")!],
        ];
        Assert.Equal(expected, Enumerable.Range(0, expected.Length).Select(c => c < row.Count ? row[c]?.Text : null));

        AssertMoment(item.GetProperty("createdAt").GetDateTimeOffset(), row[13]);
        if (item.GetProperty("updatedAt").ValueKind == JsonValueKind.Null)
        {
            Assert.True(row.Count <= 14 || row[14] is null);
        }
        else
        {
            AssertMoment(item.GetProperty("updatedAt").GetDateTimeOffset(), row[14]);
        }
    }

    /// <summary>The moment in Istanbul time; an Excel date keeps milliseconds.</summary>
    private static void AssertMoment(DateTimeOffset expected, SpreadsheetFile.CellContent? cell)
    {
        Assert.NotNull(cell);
        var local = TimeZoneInfo.ConvertTime(expected, Istanbul).DateTime;
        Assert.InRange((cell.AsDateTime() - local).Duration().TotalMilliseconds, 0, 1);
    }

    private static string[] InfoRow(SpreadsheetFile file, string label) => [label, file["Bilgi"].Value(label)!.Text];
}
