using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnterpriseInventory.Domain.Auditing;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Assets;
using EnterpriseInventory.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseInventory.IntegrationTests.Lookups;

/// <summary>
/// Day 40: renaming, deactivating and reactivating lookups (<c>PUT /api/brands/{id}</c> and the others) on SQL Server.
/// Every test adds the lookups it changes, so the tests do not depend on each other.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class LookupUpdateTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime, IDisposable
{
    private const string User = "ayse.admin";
    private const string Colleague = "mehmet.admin";

    private TestApiFactory? _api;
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        if (SqlServerDatabaseFixture.ServerConnectionString is null)
        {
            return;
        }

        _connectionString = await fixture.CreateMigratedDatabaseAsync();
        _api = new TestApiFactory(_connectionString, settings: new Dictionary<string, string?> { ["RateLimiting:PermitLimit"] = "1000" });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _api?.Dispose();

    [SqlServerTheory]
    [InlineData("/api/brands", "Brand")]
    [InlineData("/api/cities", "City")]
    [InlineData("/api/departments", "Department")]
    public async Task A_lookup_is_renamed_and_deactivated_and_each_change_is_audited_with_its_old_and_new_values(string path, string entityName)
    {
        using var client = _api!.CreateSignedInClient(User);
        var created = await CreateAsync(client, path, new { name = PersistenceTestData.Unique("Eski") });
        var id = created.GetProperty("id").GetInt32();
        var newName = PersistenceTestData.Unique("Yeni");

        var renamed = await UpdateAsync(client, path, created, name: $"  {newName} ");

        Assert.Equal(newName, renamed.GetProperty("name").GetString());
        Assert.True(renamed.GetProperty("isActive").GetBoolean());
        Assert.NotEqual(created.RowVersion(), renamed.RowVersion());

        var deactivated = await UpdateAsync(client, path, renamed, isActive: false);

        Assert.False(deactivated.GetProperty("isActive").GetBoolean());
        var listed = (await client.GetOkAsync(path)).EnumerateArray().Single(item => item.GetProperty("id").GetInt32() == id);
        Assert.Equal((newName, false), (listed.GetProperty("name").GetString(), listed.GetProperty("isActive").GetBoolean()));
        Assert.Equal(deactivated.RowVersion(), listed.RowVersion());

        var changes = await AuditAsync(entityName, id, AuditAction.Updated);
        Assert.Equal(2, changes.Count);
        Assert.All(changes, audit => Assert.Equal(User, audit.UserName));
        Assert.All(changes, audit => Assert.False(string.IsNullOrEmpty(audit.CorrelationId)));
        Assert.Equal($$"""{"name":"{{created.GetProperty("name").GetString()}}"}""", changes[0].OldValues);
        Assert.Equal($$"""{"name":"{{newName}}"}""", changes[0].NewValues);
        Assert.Equal("""{"isActive":true}""", changes[1].OldValues);
        Assert.Equal("""{"isActive":false}""", changes[1].NewValues);
    }

    [SqlServerFact]
    public async Task A_stale_row_version_is_refused_with_409_and_nothing_is_overwritten()
    {
        using var mine = _api!.CreateSignedInClient(User);
        using var theirs = _api.CreateSignedInClient(Colleague);
        var brand = await CreateAsync(mine, "/api/brands", new { name = PersistenceTestData.Unique("Marka") });
        var theirName = PersistenceTestData.Unique("Onların");

        // They save first; I still hold the version I read before.
        await UpdateAsync(theirs, "/api/brands", brand, name: theirName);
        using var response = await mine.SendWithCsrfAsync(
            HttpMethod.Put, $"/api/brands/{brand.Id()}", Body(brand, name: PersistenceTestData.Unique("Benim")));
        var problem = await AssetApi.ReadAsync(response, HttpStatusCode.Conflict);

        Assert.Equal("concurrency_conflict", problem.GetProperty("code").GetString());
        Assert.Equal("Kayıt siz düzenlerken başka bir kullanıcı tarafından değiştirildi.", problem.GetProperty("title").GetString());
        var stored = (await mine.GetOkAsync("/api/brands")).EnumerateArray().Single(item => item.Id() == brand.Id());
        Assert.Equal(theirName, stored.GetProperty("name").GetString());
        var audit = Assert.Single(await AuditAsync("Brand", brand.Id(), AuditAction.Updated));
        Assert.Equal(Colleague, audit.UserName);
    }

    [SqlServerFact]
    public async Task Two_simultaneous_changes_of_the_same_version_save_exactly_one()
    {
        using var mine = _api!.CreateSignedInClient(User);
        using var theirs = _api.CreateSignedInClient(Colleague);
        for (var round = 0; round < 5; round++)
        {
            var city = await CreateAsync(mine, "/api/cities", new { name = PersistenceTestData.Unique("Şehir") });

            var responses = await Task.WhenAll(
                mine.SendWithCsrfAsync(HttpMethod.Put, $"/api/cities/{city.Id()}", Body(city, name: PersistenceTestData.Unique("Bir"))),
                theirs.SendWithCsrfAsync(HttpMethod.Put, $"/api/cities/{city.Id()}", Body(city, isActive: false)));

            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
            Assert.Single(await AuditAsync("City", city.Id(), AuditAction.Updated));
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [SqlServerFact]
    public async Task Saving_the_same_values_changes_nothing_and_writes_no_audit_record()
    {
        using var client = _api!.CreateSignedInClient(User);
        var department = await CreateAsync(client, "/api/departments", new { name = PersistenceTestData.Unique("Departman") });

        var saved = await UpdateAsync(client, "/api/departments", department);

        Assert.Equal(department.RowVersion(), saved.RowVersion());
        Assert.Empty(await AuditAsync("Department", department.Id(), AuditAction.Updated));
    }

    [SqlServerFact]
    public async Task A_name_taken_by_another_lookup_is_refused_ignoring_case_but_a_change_of_case_is_a_rename()
    {
        using var client = _api!.CreateSignedInClient(User);
        var taken = await CreateAsync(client, "/api/brands", new { name = PersistenceTestData.Unique("Marka") });
        await UpdateAsync(client, "/api/brands", taken, isActive: false);
        var brand = await CreateAsync(client, "/api/brands", new { name = PersistenceTestData.Unique("Marka") });

        // The inactive brand's name, in capitals ("MARKA-…" has no I/i, whose Turkish capitals differ).
        using var duplicate = await client.SendWithCsrfAsync(
            HttpMethod.Put, $"/api/brands/{brand.Id()}", Body(brand, name: taken.GetProperty("name").GetString()!.ToUpperInvariant()));
        var problem = await AssetApi.ReadAsync(duplicate, HttpStatusCode.Conflict);
        Assert.Equal("duplicate_value", problem.GetProperty("code").GetString());
        Assert.Equal("Aynı adla bir marka zaten var.", problem.FieldError("name"));

        var capitals = brand.GetProperty("name").GetString()!.ToUpperInvariant();
        var renamed = await UpdateAsync(client, "/api/brands", brand, name: capitals);
        Assert.Equal(capitals, renamed.GetProperty("name").GetString());
    }

    [SqlServerFact]
    public async Task A_model_keeps_its_brand_and_its_name_is_unique_within_that_brand_only()
    {
        using var client = _api!.CreateSignedInClient(User);
        var dell = await CreateAsync(client, "/api/brands", new { name = PersistenceTestData.Unique("Dell") });
        var hp = await CreateAsync(client, "/api/brands", new { name = PersistenceTestData.Unique("Hp") });
        var latitude = await CreateAsync(client, "/api/models", new { brandId = dell.Id(), name = "Latitude 5440" });
        var optiplex = await CreateAsync(client, "/api/models", new { brandId = dell.Id(), name = "Optiplex 7010" });
        await CreateAsync(client, "/api/models", new { brandId = hp.Id(), name = "EliteBook 840" });

        using var duplicate = await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/models/{optiplex.Id()}", Body(optiplex, name: "latitude 5440"));
        Assert.Equal("Bu markada aynı adla bir model zaten var.", (await AssetApi.ReadAsync(duplicate, HttpStatusCode.Conflict)).FieldError("name"));

        // Another brand's model name is free, and a brand in the body does not move the model.
        var body = Body(latitude, name: "EliteBook 840");
        body["brandId"] = hp.Id();
        using var response = await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/models/{latitude.Id()}", body);
        var renamed = await AssetApi.ReadAsync(response, HttpStatusCode.OK);
        Assert.Equal("EliteBook 840", renamed.GetProperty("name").GetString());
        Assert.Equal(dell.Id(), renamed.GetProperty("brandId").GetInt32());
        Assert.Equal(dell.GetProperty("name").GetString(), renamed.GetProperty("brandName").GetString());
    }

    [SqlServerFact]
    public async Task A_model_or_location_is_reactivated_only_while_its_brand_or_city_is_active()
    {
        using var client = _api!.CreateSignedInClient(User);
        var brand = await CreateAsync(client, "/api/brands", new { name = PersistenceTestData.Unique("Marka") });
        var model = await UpdateAsync(client, "/api/models", await CreateAsync(client, "/api/models", new { brandId = brand.Id(), name = "Model 1" }), isActive: false);
        brand = await UpdateAsync(client, "/api/brands", brand, isActive: false);
        var city = await CreateAsync(client, "/api/cities", new { name = PersistenceTestData.Unique("Şehir") });
        var location = await UpdateAsync(client, "/api/locations", await CreateAsync(client, "/api/locations", new { cityId = city.Id(), name = "Depo" }), isActive: false);
        city = await UpdateAsync(client, "/api/cities", city, isActive: false);

        using var modelResponse = await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/models/{model.Id()}", Body(model, isActive: true));
        using var locationResponse = await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/locations/{location.Id()}", Body(location, isActive: true));

        Assert.Equal(
            "Markası pasif olan model etkinleştirilemez; önce markayı etkinleştirin.",
            (await AssetApi.ReadAsync(modelResponse, HttpStatusCode.BadRequest)).FieldError("isActive"));
        Assert.Equal(
            "Şehri pasif olan lokasyon etkinleştirilemez; önce şehri etkinleştirin.",
            (await AssetApi.ReadAsync(locationResponse, HttpStatusCode.BadRequest)).FieldError("isActive"));

        await UpdateAsync(client, "/api/brands", brand, isActive: true);
        await UpdateAsync(client, "/api/cities", city, isActive: true);
        Assert.True((await UpdateAsync(client, "/api/models", model, isActive: true)).GetProperty("isActive").GetBoolean());
        Assert.True((await UpdateAsync(client, "/api/locations", location, isActive: true)).GetProperty("isActive").GetBoolean());
    }

    public static TheoryData<string, string, string> InvalidChanges => new()
    {
        { """{"isActive":true,"rowVersion":"AAAAAAAAB9E="}""", "name", "Ad zorunludur." },
        { """{"name":" ","isActive":true,"rowVersion":"AAAAAAAAB9E="}""", "name", "Ad zorunludur." },
        { """{"name":"Ad","rowVersion":"AAAAAAAAB9E="}""", "isActive", "Durum (isActive) zorunludur." },
        { """{"name":"Ad","isActive":true}""", "rowVersion", "Kayıt sürümü (rowVersion) zorunludur." },
        { """{"name":"Ad","isActive":true,"rowVersion":"bozuk"}""", "rowVersion", "Kayıt sürümü (rowVersion) geçersiz; kaydı yeniden açıp tekrar deneyin." },
    };

    [SqlServerTheory]
    [MemberData(nameof(InvalidChanges))]
    public async Task Invalid_changes_are_refused_field_by_field_and_nothing_is_written(string json, string field, string message)
    {
        using var client = _api!.CreateSignedInClient(User);
        var brand = await CreateAsync(client, "/api/brands", new { name = PersistenceTestData.Unique("Marka") });

        using var response = await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/brands/{brand.Id()}", JsonDocument.Parse(json).RootElement);

        Assert.Equal(message, (await AssetApi.ReadAsync(response, HttpStatusCode.BadRequest)).FieldError(field));
        Assert.Empty(await AuditAsync("Brand", brand.Id(), AuditAction.Updated));
    }

    [SqlServerTheory]
    [InlineData("/api/brands")]
    [InlineData("/api/models")]
    [InlineData("/api/cities")]
    [InlineData("/api/locations")]
    [InlineData("/api/departments")]
    public async Task An_unknown_lookup_is_404(string path)
    {
        using var client = _api!.CreateSignedInClient(User);

        using var response = await client.SendWithCsrfAsync(HttpMethod.Put, $"{path}/999999", new { name = "Ad", isActive = true, rowVersion = "AAAAAAAAB9E=" });

        Assert.Equal("lookup_not_found", (await AssetApi.ReadAsync(response, HttpStatusCode.NotFound)).GetProperty("code").GetString());
    }

    [SqlServerFact]
    public async Task Changing_needs_the_csrf_token()
    {
        using var client = _api!.CreateSignedInClient(User);
        var brand = await CreateAsync(client, "/api/brands", new { name = PersistenceTestData.Unique("Marka") });

        using var response = await client.PutAsJsonAsync(AssetApi.Uri($"/api/brands/{brand.Id()}"), Body(brand, name: "Csrfsiz"));

        Assert.Equal("csrf_invalid", (await AssetApi.ReadAsync(response, HttpStatusCode.BadRequest)).GetProperty("code").GetString());
        Assert.Empty(await AuditAsync("Brand", brand.Id(), AuditAction.Updated));
    }

    private static async Task<JsonElement> CreateAsync(HttpClient client, string path, object body)
    {
        using var response = await client.SendWithCsrfAsync(HttpMethod.Post, path, body);
        return await AssetApi.ReadAsync(response, HttpStatusCode.Created);
    }

    /// <summary>Sends the lookup back with the given changes and its row version; expects <c>200</c>.</summary>
    private static async Task<JsonElement> UpdateAsync(HttpClient client, string path, JsonElement lookup, string? name = null, bool? isActive = null)
    {
        using var response = await client.SendWithCsrfAsync(HttpMethod.Put, $"{path}/{lookup.Id()}", Body(lookup, name, isActive));
        return await AssetApi.ReadAsync(response, HttpStatusCode.OK);
    }

    private static Dictionary<string, object?> Body(JsonElement lookup, string? name = null, bool? isActive = null) => new()
    {
        ["name"] = name ?? lookup.GetProperty("name").GetString(),
        ["isActive"] = isActive ?? lookup.GetProperty("isActive").GetBoolean(),
        ["rowVersion"] = lookup.RowVersion(),
    };

    private async Task<List<AuditLog>> AuditAsync(string entityName, int id, AuditAction action)
    {
        await using var db = fixture.CreateContextFor(_connectionString);
        var entityId = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return await db.AuditLogs
            .Where(a => a.EntityName == entityName && a.EntityId == entityId && a.Action == action)
            .OrderBy(a => a.Id)
            .ToListAsync();
    }
}

internal static class LookupJson
{
    public static string RowVersion(this JsonElement lookup) => lookup.GetProperty("rowVersion").GetString()!;
}
