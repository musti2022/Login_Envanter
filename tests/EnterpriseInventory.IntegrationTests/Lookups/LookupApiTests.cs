using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnterpriseInventory.Domain.Auditing;
using EnterpriseInventory.Domain.Catalog;
using EnterpriseInventory.Domain.Organization;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Assets;
using EnterpriseInventory.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseInventory.IntegrationTests.Lookups;

/// <summary>
/// The lookup lists and creating lookups, against a database of their own so every list is known: brands Dell
/// (Latitude, Optiplex) and HP (EliteBook); cities Cizre, Çine and Datça (inactive) with locations; departments
/// Muhasebe and Bilgi İşlem.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class LookupApiTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime, IDisposable
{
    private const string User = "ayse.admin";

    private TestApiFactory? _api;
    private string _connectionString = string.Empty;
    private int _dellId;
    private int _hpId;
    private int _inactiveBrandId;
    private int _cineId;
    private int _inactiveCityId;

    public async Task InitializeAsync()
    {
        if (SqlServerDatabaseFixture.ServerConnectionString is null)
        {
            return;
        }

        _connectionString = await fixture.CreateMigratedDatabaseAsync();
        var dell = Brand.Create("Dell");
        var hp = Brand.Create("HP");
        var inactiveBrand = Brand.Create("Eski Marka");
        inactiveBrand.Deactivate();
        var optiplex = AssetModel.Create(dell, "Optiplex 7010");
        var latitude = AssetModel.Create(dell, "Latitude 5440");
        var elitebook = AssetModel.Create(hp, "EliteBook 840");
        elitebook.Deactivate();

        var cine = City.Create("Çine");
        var cizre = City.Create("Cizre");
        var datca = City.Create("Datça");
        datca.Deactivate();
        var cineOffice = Location.Create(cine, "Merkez Ofis");
        var cineDepot = Location.Create(cine, "Depo");
        var cizreOffice = Location.Create(cizre, "Şube");

        await using (var context = fixture.CreateContextFor(_connectionString))
        {
            context.AddRange(optiplex, latitude, elitebook, inactiveBrand);
            context.AddRange(cineOffice, cineDepot, cizreOffice, datca);
            context.AddRange(Department.Create("Muhasebe"), Department.Create("Bilgi İşlem"));
            await context.SaveChangesAsync();
        }

        (_dellId, _hpId, _inactiveBrandId, _cineId, _inactiveCityId) = (dell.Id, hp.Id, inactiveBrand.Id, cine.Id, datca.Id);
        _api = new TestApiFactory(_connectionString, settings: new Dictionary<string, string?> { ["RateLimiting:PermitLimit"] = "1000" });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _api?.Dispose();

    [SqlServerFact]
    public async Task Lists_are_sorted_the_turkish_way_and_include_inactive_lookups()
    {
        using var client = _api!.CreateSignedInClient();

        var cities = await client.GetOkAsync("/api/cities");
        var brands = await client.GetOkAsync("/api/brands");
        var departments = await client.GetOkAsync("/api/departments");

        // Turkish order: C, Ç, D. A Latin collation would put "Çine" before "Cizre".
        Assert.Equal([("Cizre", true), ("Çine", true), ("Datça", false)], NamesAndStates(cities));
        Assert.Equal([("Dell", true), ("Eski Marka", false), ("HP", true)], NamesAndStates(brands));
        Assert.Equal([("Bilgi İşlem", true), ("Muhasebe", true)], NamesAndStates(departments));
        Assert.Equal(_cineId, cities[1].GetProperty("id").GetInt32());
    }

    [SqlServerFact]
    public async Task Models_and_locations_come_with_their_parent_and_can_be_filtered_by_it()
    {
        using var client = _api!.CreateSignedInClient();

        var allModels = await client.GetOkAsync("/api/models");
        var dellModels = await client.GetOkAsync($"/api/models?brandId={_dellId}");
        var cineLocations = await client.GetOkAsync($"/api/locations?cityId={_cineId}");
        var allLocations = await client.GetOkAsync("/api/locations");

        Assert.Equal(["Dell / Latitude 5440", "Dell / Optiplex 7010", "HP / EliteBook 840"], WithParent(allModels, "brandName"));
        Assert.Equal(["Dell / Latitude 5440", "Dell / Optiplex 7010"], WithParent(dellModels, "brandName"));
        Assert.All(dellModels.EnumerateArray(), m => Assert.Equal(_dellId, m.GetProperty("brandId").GetInt32()));
        Assert.False(allModels[2].GetProperty("isActive").GetBoolean());
        Assert.Equal(["Çine / Depo", "Çine / Merkez Ofis"], WithParent(cineLocations, "cityName"));
        Assert.Equal(["Cizre / Şube", "Çine / Depo", "Çine / Merkez Ofis"], WithParent(allLocations, "cityName"));
        Assert.Empty((await client.GetOkAsync("/api/models?brandId=999999")).EnumerateArray());
    }

    [SqlServerTheory]
    [InlineData("/api/models?brandId=0", "brandId")]
    [InlineData("/api/locations?cityId=-1", "cityId")]
    public async Task A_filter_that_is_not_a_positive_id_is_refused(string path, string field)
    {
        using var client = _api!.CreateSignedInClient();

        using var response = await client.GetAsync(AssetApi.Uri(path));
        var problem = await AssetApi.ReadAsync(response, HttpStatusCode.BadRequest);

        Assert.NotEmpty(problem.FieldError(field));
    }

    [SqlServerTheory]
    [InlineData("/api/brands", "Brand")]
    [InlineData("/api/cities", "City")]
    [InlineData("/api/departments", "Department")]
    public async Task A_lookup_is_created_trimmed_listed_and_audited(string path, string entityName)
    {
        using var client = _api!.CreateSignedInClient(User);
        var name = PersistenceTestData.Unique("Yeni");

        using var response = await client.SendWithCsrfAsync(HttpMethod.Post, path, new { name = $"  {name} " });
        var created = await AssetApi.ReadAsync(response, HttpStatusCode.Created);

        var id = created.GetProperty("id").GetInt32();
        Assert.Equal(name, created.GetProperty("name").GetString());
        Assert.True(created.GetProperty("isActive").GetBoolean());
        Assert.Contains((await client.GetOkAsync(path)).EnumerateArray(), item => item.GetProperty("id").GetInt32() == id);

        var audit = await SingleAuditAsync(entityName, id);
        Assert.Equal(AuditAction.Created, audit.Action);
        Assert.Equal(User, audit.UserName);
        Assert.Null(audit.OldValues);
        Assert.Equal(name, JsonDocument.Parse(audit.NewValues!).RootElement.GetProperty("name").GetString());
        Assert.False(string.IsNullOrEmpty(audit.CorrelationId));
    }

    [SqlServerFact]
    public async Task A_model_is_created_under_its_brand_and_audited_with_it()
    {
        using var client = _api!.CreateSignedInClient(User);

        using var response = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/models", new { brandId = _hpId, name = "ProBook 450" });
        var created = await AssetApi.ReadAsync(response, HttpStatusCode.Created);

        Assert.Equal("ProBook 450", created.GetProperty("name").GetString());
        Assert.Equal(_hpId, created.GetProperty("brandId").GetInt32());
        Assert.Equal("HP", created.GetProperty("brandName").GetString());
        var values = JsonDocument.Parse((await SingleAuditAsync("AssetModel", created.GetProperty("id").GetInt32())).NewValues!).RootElement;
        Assert.Equal(_hpId, values.GetProperty("brandId").GetInt32());
        Assert.Equal("HP", values.GetProperty("brandName").GetString());
    }

    [SqlServerFact]
    public async Task A_location_is_created_in_its_city_and_audited_with_it()
    {
        using var client = _api!.CreateSignedInClient(User);

        using var response = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/locations", new { cityId = _cineId, name = "Kat 2" });
        var created = await AssetApi.ReadAsync(response, HttpStatusCode.Created);

        Assert.Equal(_cineId, created.GetProperty("cityId").GetInt32());
        Assert.Equal("Çine", created.GetProperty("cityName").GetString());
        var values = JsonDocument.Parse((await SingleAuditAsync("Location", created.GetProperty("id").GetInt32())).NewValues!).RootElement;
        Assert.Equal("Çine", values.GetProperty("cityName").GetString());
    }

    [SqlServerTheory]
    [InlineData("/api/brands", "dell")]
    [InlineData("/api/brands", "ESKİ MARKA")]
    [InlineData("/api/cities", "ÇİNE")]
    [InlineData("/api/cities", "datça")]
    [InlineData("/api/departments", "muhasebe")]
    public async Task A_name_that_is_taken_ignoring_case_is_refused_even_by_an_inactive_lookup(string path, string name)
    {
        using var client = _api!.CreateSignedInClient(User);
        var auditsBefore = await AuditCountAsync();

        using var response = await client.SendWithCsrfAsync(HttpMethod.Post, path, new { name });
        var problem = await AssetApi.ReadAsync(response, HttpStatusCode.Conflict);

        Assert.Equal("duplicate_value", problem.GetProperty("code").GetString());
        Assert.StartsWith("Aynı adla", problem.FieldError("name"), StringComparison.Ordinal);
        Assert.Equal(auditsBefore, await AuditCountAsync());
    }

    [SqlServerFact]
    public async Task A_model_name_is_unique_within_its_brand_and_a_location_name_within_its_city()
    {
        using var client = _api!.CreateSignedInClient(User);

        // Case is ignored the Turkish way, as in every unique index: "latitude" matches "Latitude", but "LATITUDE"
        // would not, because the capital of i is İ.
        using var sameBrand = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/models", new { brandId = _dellId, name = "latitude 5440" });
        using var otherBrand = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/models", new { brandId = _hpId, name = "Latitude 5440" });
        using var sameCity = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/locations", new { cityId = _cineId, name = "depo" });

        Assert.Equal("Bu markada aynı adla bir model zaten var.", (await AssetApi.ReadAsync(sameBrand, HttpStatusCode.Conflict)).FieldError("name"));
        await AssetApi.ReadAsync(otherBrand, HttpStatusCode.Created);
        Assert.Equal("Bu şehirde aynı adla bir lokasyon zaten var.", (await AssetApi.ReadAsync(sameCity, HttpStatusCode.Conflict)).FieldError("name"));
    }

    public static TheoryData<string, object, string, string> InvalidBodies => new()
    {
        { "/api/brands", new { name = "   " }, "name", "Ad zorunludur." },
        { "/api/cities", new { }, "name", "Ad zorunludur." },
        { "/api/departments", new { name = new string('a', 101) }, "name", "Ad en fazla 100 karakter olabilir." },
        { "/api/brands", new { name = "Del\u0000l" }, "name", "Ad geçersiz karakter içeriyor." },
        { "/api/models", new { name = "X1" }, "brandId", "Marka seçilmelidir." },
        { "/api/models", new { brandId = 999999, name = "X1" }, "brandId", "Seçilen marka bulunamadı." },
        { "/api/locations", new { cityId = 0, name = "Kat 1" }, "cityId", "Şehir geçersiz." },
        { "/api/locations", new { cityId = 999999, name = "Kat 1" }, "cityId", "Seçilen şehir bulunamadı." },
    };

    [SqlServerTheory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Invalid_input_is_refused_field_by_field_and_nothing_is_written(string path, object body, string field, string message)
    {
        using var client = _api!.CreateSignedInClient(User);
        var auditsBefore = await AuditCountAsync();

        using var response = await client.SendWithCsrfAsync(HttpMethod.Post, path, body);
        var problem = await AssetApi.ReadAsync(response, HttpStatusCode.BadRequest);

        Assert.Equal(message, problem.FieldError(field));
        Assert.Equal(auditsBefore, await AuditCountAsync());
    }

    [SqlServerFact]
    public async Task Models_and_locations_cannot_be_added_under_an_inactive_brand_or_city()
    {
        using var client = _api!.CreateSignedInClient(User);

        using var model = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/models", new { brandId = _inactiveBrandId, name = "X1" });
        using var location = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/locations", new { cityId = _inactiveCityId, name = "Kat 1" });

        Assert.Equal("Seçilen marka pasif; yeni seçimlerde kullanılamaz.", (await AssetApi.ReadAsync(model, HttpStatusCode.BadRequest)).FieldError("brandId"));
        Assert.Equal("Seçilen şehir pasif; yeni seçimlerde kullanılamaz.", (await AssetApi.ReadAsync(location, HttpStatusCode.BadRequest)).FieldError("cityId"));
    }

    [SqlServerFact]
    public async Task Creating_needs_the_csrf_token()
    {
        using var client = _api!.CreateSignedInClient(User);
        var name = PersistenceTestData.Unique("Csrfsiz");

        using var response = await client.PostAsJsonAsync(AssetApi.Uri("/api/brands"), new { name });
        var problem = await AssetApi.ReadAsync(response, HttpStatusCode.BadRequest);

        Assert.Equal("csrf_invalid", problem.GetProperty("code").GetString());
        Assert.DoesNotContain((await client.GetOkAsync("/api/brands")).EnumerateArray(), b => b.GetProperty("name").GetString() == name);
    }

    private static List<(string?, bool)> NamesAndStates(JsonElement items) =>
        items.EnumerateArray().Select(i => (i.GetProperty("name").GetString(), i.GetProperty("isActive").GetBoolean())).ToList();

    private static List<string> WithParent(JsonElement items, string parentName) =>
        items.EnumerateArray().Select(i => $"{i.GetProperty(parentName).GetString()} / {i.GetProperty("name").GetString()}").ToList();

    private async Task<AuditLog> SingleAuditAsync(string entityName, int id)
    {
        await using var db = fixture.CreateContextFor(_connectionString);
        var entityId = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return await db.AuditLogs.SingleAsync(a => a.EntityName == entityName && a.EntityId == entityId);
    }

    private async Task<int> AuditCountAsync()
    {
        await using var db = fixture.CreateContextFor(_connectionString);
        return await db.AuditLogs.CountAsync();
    }
}
