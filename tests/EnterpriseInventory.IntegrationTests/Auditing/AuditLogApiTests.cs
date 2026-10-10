using System.Globalization;
using System.Net;
using System.Text.Json;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Assets;
using EnterpriseInventory.IntegrationTests.Persistence;

namespace EnterpriseInventory.IntegrationTests.Auditing;

/// <summary>
/// <c>GET /api/audit-logs</c> on a database of its own, so every record in it was written by these tests through the
/// API: the old and new values of each change can be followed field by field, and the filters find them.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class AuditLogApiTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime, IDisposable
{
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
        _api = new TestApiFactory(_connectionString, settings: new Dictionary<string, string?>(LoginEndpointTests.FakeDirectory) { ["RateLimiting:PermitLimit"] = "1000" });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _api?.Dispose();

    [SqlServerFact]
    public async Task An_edit_can_be_followed_field_by_field_with_its_old_and_new_values()
    {
        using var client = _api!.CreateSignedInClient("ayse.admin");
        var asset = await client.CreateAssetAsync(_references!.NewAssetBody("IZ-0001"));
        var body = asset.ToUpdateBody();
        body["computerName"] = "PC-YENI";
        body["description"] = null;
        body["status"] = "Faulty";
        body["cityId"] = _references.SecondCityId;
        body["locationId"] = _references.SecondCityLocationId;

        using var update = await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{asset.Id()}", body);
        await AssetApi.ReadAsync(update, HttpStatusCode.OK);
        var correlationId = update.Headers.GetValues("X-Correlation-ID").Single();

        var log = await client.GetOkAsync($"/api/audit-logs?entityName=Asset&entityId={asset.Id()}");

        var items = log.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["StatusChanged", "LocationChanged", "Updated", "Created"], items.Select(i => i.GetProperty("action").GetString()));
        Assert.All(items, item =>
        {
            Assert.Equal("Asset", item.GetProperty("entityName").GetString());
            Assert.Equal(asset.Id().ToString(CultureInfo.InvariantCulture), item.GetProperty("entityId").GetString());
            Assert.Equal("IZ-0001", item.GetProperty("entityLabel").GetString());
            Assert.Equal("ayse.admin", item.GetProperty("userName").GetString());
        });

        // One request, one correlation ID, one moment: the three records of the edit belong together.
        var edit = items.Take(3).ToList();
        Assert.All(edit, item => Assert.Equal(correlationId, item.GetProperty("correlationId").GetString()));
        Assert.Single(edit.Select(item => item.GetProperty("timestamp").GetDateTimeOffset()).Distinct());

        Assert.Equal(
            """{"status":"Available"}|{"status":"Faulty"}""",
            Values(edit[0]));
        Assert.Equal(
            $$"""{"cityId":{{_references.CityId}},"locationId":{{_references.LocationId}}}|{"cityId":{{_references.SecondCityId}},"locationId":{{_references.SecondCityLocationId}}}""",
            Values(edit[1], "cityId", "locationId"));
        Assert.Equal(
            """{"computerName":"PC-TEST","description":"Test demirbaşı"}|{"computerName":"PC-YENI","description":null}""",
            Values(edit[2]));

        // What the asset was created with: no old values, every field new.
        var created = items[3];
        Assert.Equal(JsonValueKind.Null, created.GetProperty("oldValues").ValueKind);
        Assert.Equal("IZ-0001", created.GetProperty("newValues").GetProperty("assetCode").GetString());
        Assert.Equal("PC-TEST", created.GetProperty("newValues").GetProperty("computerName").GetString());
        Assert.Equal(_references.BrandName, created.GetProperty("newValues").GetProperty("brandName").GetString());
    }

    [SqlServerFact]
    public async Task Assignments_returns_and_moves_record_the_holder_and_the_place_before_and_after()
    {
        using var client = _api!.CreateSignedInClient("mehmet.admin");
        var asset = await client.CreateAssetAsync(_references!.NewAssetBody("IZ-0002"));
        var employee = await client.EmployeeGuidAsync("dev.user");
        using var assign = await client.PostAssignmentAsync(asset.Id(), asset.AssignBody(employee, "Saha dizüstüsü", "Çantasıyla verildi"));
        var assigned = await AssetApi.ReadAsync(assign, HttpStatusCode.Created);
        using var giveBack = await client.PostReturnAsync(asset.Id(), assigned.GetProperty("rowVersion").GetString());
        await AssetApi.ReadAsync(giveBack, HttpStatusCode.OK);

        var log = await client.GetOkAsync("/api/audit-logs?assetCode=IZ-0002&action=Assigned&action=Returned");

        var items = log.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["Returned", "Assigned"], items.Select(i => i.GetProperty("action").GetString()));
        Assert.All(items, i => Assert.Equal("mehmet.admin", i.GetProperty("userName").GetString()));
        var returned = items[0];
        Assert.Equal("Assigned", returned.GetProperty("oldValues").GetProperty("status").GetString());
        Assert.Equal("dev.user", returned.GetProperty("oldValues").GetProperty("employeeUserName").GetString());
        Assert.Equal("Available", returned.GetProperty("newValues").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, returned.GetProperty("newValues").GetProperty("returnedAt").ValueKind);
        var assignment = items[1];
        Assert.Equal("Available", assignment.GetProperty("oldValues").GetProperty("status").GetString());
        Assert.Equal("Assigned", assignment.GetProperty("newValues").GetProperty("status").GetString());
        Assert.Equal("dev.user", assignment.GetProperty("newValues").GetProperty("employeeUserName").GetString());
        Assert.Equal("Saha dizüstüsü", assignment.GetProperty("newValues").GetProperty("assignmentDescription").GetString());
        Assert.Equal("Çantasıyla verildi", assignment.GetProperty("newValues").GetProperty("notes").GetString());
    }

    [SqlServerFact]
    public async Task Filters_find_records_by_user_action_asset_code_request_and_time()
    {
        using var ayse = _api!.CreateSignedInClient("ayse.admin");
        using var mehmet = _api.CreateSignedInClient("mehmet.admin");
        var istanbul = await ayse.CreateAssetAsync(_references!.NewAssetBody("PC-IST-01"));
        await mehmet.CreateAssetAsync(_references.NewAssetBody("PC-ANK-01"));
        var body = istanbul.ToUpdateBody();
        body["computerName"] = "PC-DEGISTI";
        using var update = await mehmet.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{istanbul.Id()}", body);
        await AssetApi.ReadAsync(update, HttpStatusCode.OK);
        var correlationId = update.Headers.GetValues("X-Correlation-ID").Single();

        // Asset codes and user names are found by part, ignoring case the way people type them ("ist" finds "IST").
        Assert.Equal(["Updated:mehmet.admin", "Created:ayse.admin"], await Found(ayse, "assetCode=pc-ist"));
        Assert.Equal(["Updated:mehmet.admin", "Created:mehmet.admin"], await Found(ayse, "entityName=Asset&userName=MEHMET&assetCode=PC-"));
        Assert.Equal(["Created:mehmet.admin", "Created:ayse.admin"], await Found(ayse, "action=Created&assetCode=PC-"));
        Assert.Equal(["Updated:mehmet.admin"], await Found(ayse, $"correlationId={correlationId}"));
        Assert.Empty(await Found(ayse, "assetCode=YOK-BOYLE-BIR-KOD"));

        // A time window: from is inclusive, to is exclusive.
        var all = (await ayse.GetOkAsync("/api/audit-logs?assetCode=PC-&pageSize=100")).GetProperty("items").EnumerateArray()
            .Select(i => (Id: i.GetProperty("id").GetInt64(), At: i.GetProperty("timestamp").GetDateTimeOffset()))
            .ToList();
        var middle = all[1].At;
        var from = Uri.EscapeDataString(middle.ToString("O", CultureInfo.InvariantCulture));
        var since = (await ayse.GetOkAsync($"/api/audit-logs?assetCode=PC-&pageSize=100&from={from}")).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetInt64());
        var before = (await ayse.GetOkAsync($"/api/audit-logs?assetCode=PC-&pageSize=100&to={from}")).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetInt64());
        Assert.Equal(all.Where(r => r.At >= middle).Select(r => r.Id), since);
        Assert.Equal(all.Where(r => r.At < middle).Select(r => r.Id), before);
    }

    [SqlServerFact]
    public async Task Lookups_are_listed_under_their_current_names_and_archived_assets_stay_traceable()
    {
        using var client = _api!.CreateSignedInClient();
        var brandName = PersistenceTestData.Unique("Denetim marka");
        using (var brand = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/brands", new { name = brandName }))
        {
            await AssetApi.ReadAsync(brand, HttpStatusCode.Created);
        }

        var asset = await client.CreateAssetAsync(_references!.NewAssetBody("IZ-ARSIV"));
        using (var archive = await client.SendWithCsrfAsync(
            HttpMethod.Delete, $"/api/assets/{asset.Id()}?rowVersion={Uri.EscapeDataString(asset.GetProperty("rowVersion").GetString()!)}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        }

        var brands = (await client.GetOkAsync("/api/audit-logs?entityName=brand")).GetProperty("items").EnumerateArray().ToList();
        var created = Assert.Single(brands, b => b.GetProperty("entityLabel").GetString() == brandName);
        Assert.Equal("Brand", created.GetProperty("entityName").GetString());
        Assert.Equal(brandName, created.GetProperty("newValues").GetProperty("name").GetString());

        var archived = (await client.GetOkAsync($"/api/audit-logs?assetCode=IZ-ARSIV")).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["Archived", "Created"], archived.Select(i => i.GetProperty("action").GetString()));
        Assert.All(archived, i => Assert.Equal("IZ-ARSIV", i.GetProperty("entityLabel").GetString()));
        Assert.False(archived[0].GetProperty("oldValues").GetProperty("isArchived").GetBoolean());
        Assert.True(archived[0].GetProperty("newValues").GetProperty("isArchived").GetBoolean());
    }

    [SqlServerFact]
    public async Task Sign_ins_are_listed_without_the_password()
    {
        await using var signInApi = new TestApiFactory(_connectionString, useTestAuthentication: false, settings: LoginEndpointTests.FakeDirectory);
        using (var browser = signInApi.CreateAnonymousClient())
        {
            await browser.SignInAsync("dev.admin", LoginEndpointTests.FakePassword);
        }

        using var client = _api!.CreateSignedInClient();
        using var response = await client.GetAsync(AssetApi.Uri("/api/audit-logs?entityName=AdminUser&action=SignedIn"));
        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var signIn = JsonDocument.Parse(text).RootElement.GetProperty("items").EnumerateArray().First();
        Assert.Equal("dev.admin", signIn.GetProperty("entityLabel").GetString());
        Assert.Equal("dev.admin", signIn.GetProperty("userName").GetString());
        Assert.Equal("dev.admin", signIn.GetProperty("newValues").GetProperty("SamAccountName").GetString());
        Assert.DoesNotContain(LoginEndpointTests.FakePassword, text, StringComparison.Ordinal);
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
    }

    [SqlServerFact]
    public async Task Pages_hold_every_record_once_newest_first_and_one_record_can_be_read_by_its_id()
    {
        using var client = _api!.CreateSignedInClient();
        for (var i = 1; i <= 5; i++)
        {
            await client.CreateAssetAsync(_references!.NewAssetBody($"SAYFA-{i}"));
        }

        var pages = new List<JsonElement>();
        for (var page = 1; page <= 3; page++)
        {
            pages.Add(await client.GetOkAsync($"/api/audit-logs?assetCode=SAYFA-&pageSize=2&page={page}"));
        }

        Assert.All(pages, p => Assert.Equal(5, p.GetProperty("totalCount").GetInt32()));
        Assert.All(pages, p => Assert.Equal(3, p.GetProperty("totalPages").GetInt32()));
        var labels = pages.SelectMany(p => p.GetProperty("items").EnumerateArray()).Select(i => i.GetProperty("entityLabel").GetString()).ToList();
        Assert.Equal(["SAYFA-5", "SAYFA-4", "SAYFA-3", "SAYFA-2", "SAYFA-1"], labels);

        var first = pages[0].GetProperty("items")[0];
        var single = await client.GetOkAsync($"/api/audit-logs/{first.GetProperty("id").GetInt64()}");
        Assert.Equal(first.GetRawText(), single.GetRawText());

        using var missing = await client.GetAsync(AssetApi.Uri("/api/audit-logs/999999999"));
        var problem = await AssetApi.ReadAsync(missing, HttpStatusCode.NotFound);
        Assert.Equal("audit_log_not_found", problem.GetProperty("code").GetString());
        Assert.Equal("Denetim kaydı bulunamadı.", problem.GetProperty("title").GetString());
    }

    [SqlServerTheory]
    [InlineData("entityName=Kullanici", "entityName", "Kayıt türü geçersiz.")]
    [InlineData("entityId=12", "entityId", "Kayıt kimliği yalnızca kayıt türüyle birlikte aranabilir.")]
    [InlineData("entityName=Brand&assetCode=PC", "assetCode", "Demirbaş kodu yalnızca demirbaş kayıtlarında aranabilir.")]
    [InlineData("action=Deleted", "action", "İşlem filtresi geçersiz.")]
    [InlineData("action=2", "action", "İşlem filtresi geçersiz.")]
    [InlineData("from=2026-10-01", "from", "Başlangıç zamanı geçersiz.")]
    [InlineData("from=2026-10-01T00:00:00", "from", "Başlangıç zamanı geçersiz.")]
    [InlineData("to=yarin", "to", "Bitiş zamanı geçersiz.")]
    [InlineData("from=2026-10-02T00:00:00Z&to=2026-10-01T00:00:00Z", "to", "Bitiş zamanı başlangıç zamanından sonra olmalıdır.")]
    [InlineData("from=2026-10-02T00:00:00%2B03:00&to=2026-10-01T21:00:00Z", "to", "Bitiş zamanı başlangıç zamanından sonra olmalıdır.")]
    [InlineData("pageSize=101", "pageSize", "Sayfa boyutu 1 ile 100 arasında olmalıdır.")]
    [InlineData("page=0", "page", "Sayfa numarası 1 ile")]
    public async Task Invalid_filters_are_refused_with_a_turkish_message_under_the_field(string query, string field, string message)
    {
        using var client = _api!.CreateSignedInClient();

        using var response = await client.GetAsync(AssetApi.Uri($"/api/audit-logs?{query}"));

        var problem = await AssetApi.ReadAsync(response, HttpStatusCode.BadRequest);
        Assert.StartsWith(message, problem.FieldError(field), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/audit-logs")]
    [InlineData("/api/audit-logs/1")]
    public async Task Visitors_get_401_and_users_without_the_administrator_role_get_403(string path)
    {
        await using var api = new TestApiFactory();
        using var visitor = api.CreateAnonymousClient();
        using var reader = api.CreateSignedInClient("veli.user", roles: "Reader");

        using var anonymous = await visitor.GetAsync(AssetApi.Uri(path));
        using var forbidden = await reader.GetAsync(AssetApi.Uri(path));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [SqlServerTheory]
    [InlineData("POST", "/api/audit-logs")]
    [InlineData("PUT", "/api/audit-logs/1")]
    [InlineData("DELETE", "/api/audit-logs/1")]
    public async Task Records_cannot_be_written_changed_or_deleted_through_the_api(string method, string path)
    {
        using var client = _api!.CreateSignedInClient();

        using var response = await client.SendWithCsrfAsync(new HttpMethod(method), path, new { action = "Created" });

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    /// <summary>"old|new" JSON of an entry, keeping only <paramref name="fields"/> when given.</summary>
    private static string Values(JsonElement entry, params string[] fields)
    {
        string Pick(JsonElement values) => fields.Length == 0
            ? values.GetRawText()
            : JsonSerializer.Serialize(fields.ToDictionary(f => f, f => values.GetProperty(f)));
        return $"{Pick(entry.GetProperty("oldValues"))}|{Pick(entry.GetProperty("newValues"))}";
    }

    private static async Task<List<string>> Found(HttpClient client, string query) =>
        (await client.GetOkAsync($"/api/audit-logs?{query}")).GetProperty("items").EnumerateArray()
            .Select(i => $"{i.GetProperty("action").GetString()}:{i.GetProperty("userName").GetString()}")
            .ToList();
}
