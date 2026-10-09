using System.Globalization;
using System.Net;
using System.Text.Json;
using EnterpriseInventory.Application.Assets;
using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Domain.Auditing;
using EnterpriseInventory.Domain.Catalog;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseInventory.IntegrationTests.Assets;

/// <summary>
/// <c>PUT /api/assets/{id}</c>: an edit of the version the caller read is saved and audited per kind of change; an
/// edit of an older version gets 409 and changes nothing, so nobody silently overwrites someone else's work.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class AssetUpdateTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime, IDisposable
{
    private const string User = "ayse.admin";
    private const string OtherUser = "mehmet.admin";

    private TestApiFactory? _api;
    private InventoryReferences _refs = null!;

    public async Task InitializeAsync()
    {
        if (SqlServerDatabaseFixture.ServerConnectionString is null)
        {
            return;
        }

        _refs = await InventoryReferences.SeedAsync(fixture);
        _api = new TestApiFactory(fixture.ConnectionString, settings: new Dictionary<string, string?> { ["RateLimiting:PermitLimit"] = "1000" });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _api?.Dispose();

    [SqlServerFact]
    public async Task An_update_of_the_current_version_is_saved_and_returns_the_new_version()
    {
        using var client = _api!.CreateSignedInClient(User);
        var created = await client.CreateAssetAsync(_refs.NewAssetBody());
        var body = created.ToUpdateBody();
        body["computerName"] = " PC-YENI ";
        body["description"] = null;
        body["modelId"] = _refs.SecondModelId;
        body["locationId"] = _refs.SecondLocationId;
        body["status"] = "Faulty";

        using var editor = _api.CreateSignedInClient(OtherUser);
        using var response = await editor.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{created.Id()}", body);
        var updated = await AssetApi.ReadAsync(response, HttpStatusCode.OK);

        Assert.Equal("PC-YENI", updated.GetProperty("computerName").GetString());
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("description").ValueKind);
        Assert.Equal(_refs.SecondModelId, updated.GetProperty("model").GetProperty("id").GetInt32());
        Assert.Equal(_refs.BrandName, updated.GetProperty("brand").GetProperty("name").GetString());
        Assert.Equal(_refs.SecondLocationId, updated.GetProperty("location").GetProperty("id").GetInt32());
        Assert.Equal("Faulty", updated.GetProperty("status").GetString());
        Assert.Equal(User, updated.GetProperty("createdBy").GetString());
        Assert.Equal(OtherUser, updated.GetProperty("updatedBy").GetString());
        Assert.NotEqual(created.GetProperty("rowVersion").GetString(), updated.GetProperty("rowVersion").GetString());

        var readBack = await client.GetOkAsync($"/api/assets/{created.Id()}");
        Assert.Equal(updated.GetProperty("rowVersion").GetString(), readBack.GetProperty("rowVersion").GetString());
        Assert.Equal("PC-YENI", readBack.GetProperty("computerName").GetString());
    }

    [SqlServerFact]
    public async Task Each_kind_of_change_gets_its_own_audit_record_with_old_and_new_values()
    {
        using var client = _api!.CreateSignedInClient(User);
        var created = await client.CreateAssetAsync(_refs.NewAssetBody());
        var body = created.ToUpdateBody();
        body["computerName"] = "PC-DEGISTI";
        body["departmentId"] = _refs.SecondDepartmentId;
        body["status"] = "Retired";

        using var response = await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{created.Id()}", body);
        await AssetApi.ReadAsync(response, HttpStatusCode.OK);

        var records = await AuditRecordsAsync(created.Id());
        Assert.Equal([AuditAction.Created, AuditAction.Updated, AuditAction.LocationChanged, AuditAction.StatusChanged], records.Select(r => r.Action));
        var correlationId = response.Headers.GetValues("X-Correlation-ID").Single();
        Assert.All(records.Skip(1), r =>
        {
            Assert.Equal(User, r.UserName);
            Assert.Equal(correlationId, r.CorrelationId);
        });

        Assert.Equal("""{"computerName":"PC-TEST"}""", records[1].OldValues);
        Assert.Equal("""{"computerName":"PC-DEGISTI"}""", records[1].NewValues);
        var oldPlace = JsonDocument.Parse(records[2].OldValues!).RootElement;
        var newPlace = JsonDocument.Parse(records[2].NewValues!).RootElement;
        Assert.Equal(_refs.DepartmentId, oldPlace.GetProperty("departmentId").GetInt32());
        Assert.Equal(_refs.SecondDepartmentId, newPlace.GetProperty("departmentId").GetInt32());
        Assert.StartsWith("Birim-", newPlace.GetProperty("departmentName").GetString(), StringComparison.Ordinal);
        Assert.False(newPlace.TryGetProperty("cityId", out _), "Only the fields that changed are recorded.");
        Assert.Equal("""{"status":"Available"}""", records[3].OldValues);
        Assert.Equal("""{"status":"Retired"}""", records[3].NewValues);
    }

    [SqlServerFact]
    public async Task An_update_that_changes_nothing_writes_nothing()
    {
        using var client = _api!.CreateSignedInClient(User);
        var created = await client.CreateAssetAsync(_refs.NewAssetBody());
        var body = created.ToUpdateBody();
        body["computerName"] = $"  {body["computerName"]}  ";
        body["assetType"] = "laptop";

        using var response = await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{created.Id()}", body);
        var updated = await AssetApi.ReadAsync(response, HttpStatusCode.OK);

        Assert.Equal(created.GetProperty("rowVersion").GetString(), updated.GetProperty("rowVersion").GetString());
        Assert.Equal([AuditAction.Created], (await AuditRecordsAsync(created.Id())).Select(r => r.Action));
    }

    [SqlServerFact]
    public async Task An_update_of_an_older_version_gets_409_and_keeps_what_the_other_user_saved()
    {
        using var first = _api!.CreateSignedInClient(User);
        using var second = _api.CreateSignedInClient(OtherUser);
        var created = await first.CreateAssetAsync(_refs.NewAssetBody());
        var firstEdit = created.ToUpdateBody();
        firstEdit["description"] = "Ayşe'nin düzenlemesi";
        var secondEdit = created.ToUpdateBody();
        secondEdit["description"] = "Mehmet'in düzenlemesi";
        secondEdit["status"] = "Faulty";

        using (var response = await first.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{created.Id()}", firstEdit))
        {
            await AssetApi.ReadAsync(response, HttpStatusCode.OK);
        }

        using var stale = await second.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{created.Id()}", secondEdit);
        var problem = await AssetApi.ReadAsync(stale, HttpStatusCode.Conflict);

        Assert.Equal("concurrency_conflict", problem.GetProperty("code").GetString());
        Assert.Equal("Kayıt siz düzenlerken başka bir kullanıcı tarafından değiştirildi.", problem.GetProperty("title").GetString());
        Assert.Equal(
            "Değişiklikleriniz kaydedilmedi. Kaydı yeniden açıp güncel bilgiler üzerinde tekrar deneyin.",
            problem.GetProperty("detail").GetString());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("correlationId").GetString()));

        var current = await first.GetOkAsync($"/api/assets/{created.Id()}");
        Assert.Equal("Ayşe'nin düzenlemesi", current.GetProperty("description").GetString());
        Assert.Equal("Available", current.GetProperty("status").GetString());
        Assert.Equal(User, current.GetProperty("updatedBy").GetString());
        Assert.Equal([AuditAction.Created, AuditAction.Updated], (await AuditRecordsAsync(created.Id())).Select(r => r.Action));
    }

    [SqlServerFact]
    public async Task A_made_up_row_version_gets_409()
    {
        using var client = _api!.CreateSignedInClient(User);
        var created = await client.CreateAssetAsync(_refs.NewAssetBody());
        var body = created.ToUpdateBody();
        body["description"] = "Tahmin edilen sürüm";
        body["rowVersion"] = Convert.ToBase64String(new byte[8]);

        using var response = await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{created.Id()}", body);

        Assert.Equal("concurrency_conflict", (await AssetApi.ReadAsync(response, HttpStatusCode.Conflict)).GetProperty("code").GetString());
    }

    [SqlServerFact]
    public async Task Simultaneous_updates_of_the_same_version_save_exactly_one()
    {
        using var setup = _api!.CreateSignedInClient(User);
        var created = await setup.CreateAssetAsync(_refs.NewAssetBody());
        var clients = Enumerable.Range(0, 5).Select(i => _api.CreateSignedInClient($"editor{i}")).ToList();
        try
        {
            var responses = await Task.WhenAll(clients.Select((client, i) =>
            {
                var body = created.ToUpdateBody();
                body["computerName"] = $"PC-YARIS-{i}";
                return client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{created.Id()}", body);
            }));

            var winner = await AssetApi.ReadAsync(Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK), HttpStatusCode.OK);
            foreach (var loser in responses.Where(r => r.StatusCode != HttpStatusCode.OK))
            {
                Assert.Equal("concurrency_conflict", (await AssetApi.ReadAsync(loser, HttpStatusCode.Conflict)).GetProperty("code").GetString());
            }

            foreach (var response in responses)
            {
                response.Dispose();
            }

            var current = await setup.GetOkAsync($"/api/assets/{created.Id()}");
            Assert.Equal(winner.GetProperty("computerName").GetString(), current.GetProperty("computerName").GetString());
            Assert.Equal([AuditAction.Created, AuditAction.Updated], (await AuditRecordsAsync(created.Id())).Select(r => r.Action));
        }
        finally
        {
            clients.ForEach(client => client.Dispose());
        }
    }

    [SqlServerTheory]
    [InlineData("rowVersion", null, "Kayıt sürümü (rowVersion) zorunludur.")]
    [InlineData("rowVersion", "AAAA", "Kayıt sürümü (rowVersion) geçersiz; kaydı yeniden açıp tekrar deneyin.")]
    [InlineData("status", null, "Durum zorunludur.")]
    [InlineData("status", "Assigned", AssetMessages.StatusAssignedOnlyByAssignment)]
    [InlineData("assetCode", " ", "Demirbaş kodu zorunludur.")]
    [InlineData("modelId", null, "Model seçilmelidir.")]
    public async Task Invalid_updates_are_refused_with_a_turkish_message_and_nothing_changes(string field, string? value, string message)
    {
        using var client = _api!.CreateSignedInClient(User);
        var created = await client.CreateAssetAsync(_refs.NewAssetBody());
        var body = created.ToUpdateBody();
        body["description"] = "Kaydedilmemeli";
        body[field] = value;

        using var response = await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{created.Id()}", body);
        var problem = await AssetApi.ReadAsync(response, HttpStatusCode.BadRequest);

        Assert.Equal(message, problem.FieldError(field));
        await AssertUnchangedAsync(client, created);
    }

    [SqlServerFact]
    public async Task A_kept_lookup_may_have_been_deactivated_but_a_newly_chosen_one_must_be_active()
    {
        using var client = _api!.CreateSignedInClient(User);
        var model = AssetModel.Create(Brand.Create(PersistenceTestData.Unique("Marka")), PersistenceTestData.Unique("Model"));
        await fixture.SaveAsync(model);
        var body = _refs.NewAssetBody();
        body["modelId"] = model.Id;
        var created = await client.CreateAssetAsync(body);
        await using (var db = fixture.CreateContext())
        {
            (await db.AssetModels.SingleAsync(m => m.Id == model.Id)).Deactivate();
            await db.SaveChangesAsync();
        }

        var keep = created.ToUpdateBody();
        keep["description"] = "Pasif model korunur";
        using var kept = await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{created.Id()}", keep);
        var updated = await AssetApi.ReadAsync(kept, HttpStatusCode.OK);

        var choose = updated.ToUpdateBody();
        choose["modelId"] = _refs.InactiveModelId;
        using var chosen = await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{created.Id()}", choose);

        Assert.Equal(AssetMessages.ModelInactive, (await AssetApi.ReadAsync(chosen, HttpStatusCode.BadRequest)).FieldError("modelId"));
        Assert.Equal(model.Id, updated.GetProperty("model").GetProperty("id").GetInt32());
    }

    [SqlServerFact]
    public async Task Another_assets_code_or_serial_number_is_refused_but_an_asset_keeps_its_own()
    {
        using var client = _api!.CreateSignedInClient(User);
        var first = await client.CreateAssetAsync(_refs.NewAssetBody());
        var second = await client.CreateAssetAsync(_refs.NewAssetBody());

        var taken = second.ToUpdateBody();
        taken["assetCode"] = first.GetProperty("assetCode").GetString()!.ToLowerInvariant();
        taken["serialNumber"] = first.GetProperty("serialNumber").GetString();
        using var refused = await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{second.Id()}", taken);
        var problem = await AssetApi.ReadAsync(refused, HttpStatusCode.Conflict);
        Assert.Equal("duplicate_value", problem.GetProperty("code").GetString());
        Assert.Equal(AssetMessages.AssetCodeTaken, problem.FieldError("assetCode"));
        Assert.Equal(AssetMessages.SerialNumberTaken, problem.FieldError("serialNumber"));
        await AssertUnchangedAsync(client, second);

        var ownInLowerCase = second.ToUpdateBody();
        ownInLowerCase["assetCode"] = second.GetProperty("assetCode").GetString()!.ToLowerInvariant();
        using var kept = await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{second.Id()}", ownInLowerCase);
        Assert.Equal(ownInLowerCase["assetCode"], (await AssetApi.ReadAsync(kept, HttpStatusCode.OK)).GetProperty("assetCode").GetString());
    }

    [SqlServerFact]
    public async Task An_assigned_asset_keeps_its_status_while_edited_and_cannot_change_it()
    {
        using var client = _api!.CreateSignedInClient(User);
        var id = await SeedAsync(asset => asset.Assign(
            PersistenceTestData.NewEmployee(fixture.Clock.GetUtcNow()), "Zimmet", notes: null, "mehmet.admin", fixture.Clock.GetUtcNow()));
        var assigned = await client.GetOkAsync($"/api/assets/{id}");

        var edit = assigned.ToUpdateBody();
        edit["description"] = "Zimmetliyken düzenlendi";
        using var edited = await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{id}", edit);
        var updated = await AssetApi.ReadAsync(edited, HttpStatusCode.OK);
        Assert.Equal("Assigned", updated.GetProperty("status").GetString());
        Assert.Equal("Zimmet", updated.GetProperty("activeAssignment").GetProperty("assignmentDescription").GetString());

        var statusChange = updated.ToUpdateBody();
        statusChange["status"] = "Faulty";
        using var refused = await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{id}", statusChange);
        Assert.Equal("asset_assigned", (await AssetApi.ReadAsync(refused, HttpStatusCode.Conflict)).GetProperty("code").GetString());
        await AssertUnchangedAsync(client, updated);
    }

    [SqlServerFact]
    public async Task An_archived_asset_cannot_be_edited()
    {
        using var client = _api!.CreateSignedInClient(User);
        var id = await SeedAsync(asset => asset.Archive());
        var archived = await client.GetOkAsync($"/api/assets/{id}");
        var body = archived.ToUpdateBody();
        body["description"] = "Arşivden sonra";

        using var response = await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{id}", body);
        var problem = await AssetApi.ReadAsync(response, HttpStatusCode.Conflict);

        Assert.Equal("asset_archived", problem.GetProperty("code").GetString());
        Assert.Equal("Arşivlenmiş demirbaş değiştirilemez.", problem.GetProperty("title").GetString());
        await AssertUnchangedAsync(client, archived);
    }

    [SqlServerFact]
    public async Task An_unknown_asset_gets_404()
    {
        using var client = _api!.CreateSignedInClient(User);
        var body = _refs.NewAssetBody();
        body["status"] = "Available";
        body["rowVersion"] = Convert.ToBase64String(new byte[8]);

        using var response = await client.SendWithCsrfAsync(HttpMethod.Put, "/api/assets/987654321", body);

        Assert.Equal("asset_not_found", (await AssetApi.ReadAsync(response, HttpStatusCode.NotFound)).GetProperty("code").GetString());
    }

    [SqlServerFact]
    public async Task An_update_without_the_csrf_token_is_refused()
    {
        using var client = _api!.CreateSignedInClient(User);
        var created = await client.CreateAssetAsync(_refs.NewAssetBody());
        var body = created.ToUpdateBody();
        body["description"] = "CSRF olmadan";

        using var response = await client.PutAsync(AssetApi.Uri($"/api/assets/{created.Id()}"), System.Net.Http.Json.JsonContent.Create(body));

        Assert.Equal("csrf_invalid", (await AssetApi.ReadAsync(response, HttpStatusCode.BadRequest)).GetProperty("code").GetString());
        await AssertUnchangedAsync(client, created);
    }

    /// <summary>Saves an asset on the seeded lookups after <paramref name="prepare"/> and returns its ID.</summary>
    private async Task<int> SeedAsync(Action<Asset> prepare)
    {
        await using var db = fixture.CreateContext();
        var asset = Asset.Create(
            PersistenceTestData.Unique("DMR")[..30],
            AssetType.Desktop,
            await db.AssetModels.Include(m => m.Brand).SingleAsync(m => m.Id == _refs.ModelId),
            await db.Cities.SingleAsync(c => c.Id == _refs.CityId),
            await db.Departments.SingleAsync(d => d.Id == _refs.DepartmentId),
            computerName: "PC-SEED",
            serialNumber: PersistenceTestData.Unique("SN"));
        prepare(asset);
        db.Add(asset);
        await db.SaveChangesAsync();
        return asset.Id;
    }

    private async Task<List<AuditLog>> AuditRecordsAsync(int assetId)
    {
        var id = assetId.ToString(CultureInfo.InvariantCulture);
        await using var db = fixture.CreateContext();
        return await db.AuditLogs.Where(a => a.EntityName == "Asset" && a.EntityId == id).OrderBy(a => a.Id).ToListAsync();
    }

    private static async Task AssertUnchangedAsync(HttpClient client, JsonElement before)
    {
        var current = await client.GetOkAsync($"/api/assets/{before.Id()}");
        Assert.Equal(before.GetProperty("rowVersion").GetString(), current.GetProperty("rowVersion").GetString());
        Assert.Equal(before.GetProperty("description").ToString(), current.GetProperty("description").ToString());
        Assert.Equal(before.GetProperty("status").GetString(), current.GetProperty("status").GetString());
    }
}
