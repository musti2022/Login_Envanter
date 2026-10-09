using System.Net;
using System.Text.Json;
using EnterpriseInventory.Application.Assets;
using EnterpriseInventory.Domain.Auditing;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseInventory.IntegrationTests.Assets;

/// <summary><c>POST /api/assets</c>: valid assets are created and audited; invalid ones are refused and leave nothing behind.</summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class AssetCreateTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime, IDisposable
{
    private const string User = "ayse.admin";

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
    public async Task A_valid_asset_is_created_and_can_be_read_back()
    {
        using var client = _api!.CreateSignedInClient(User);
        var body = _refs.NewAssetBody();
        var code = (string)body["assetCode"]!;
        body["assetCode"] = $"  {code} ";
        body["assetType"] = "laptop";
        body["computerName"] = " PC-01 ";

        using var response = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/assets", body);
        var created = await AssetApi.ReadAsync(response, HttpStatusCode.Created);

        var id = created.GetProperty("id").GetInt32();
        Assert.Equal($"/api/assets/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal(code, created.GetProperty("assetCode").GetString());
        Assert.Equal("PC-01", created.GetProperty("computerName").GetString());
        Assert.Equal("Laptop", created.GetProperty("assetType").GetString());
        Assert.Equal("Available", created.GetProperty("status").GetString());
        Assert.Equal(_refs.BrandName, created.GetProperty("brand").GetProperty("name").GetString());
        Assert.Equal(_refs.ModelId, created.GetProperty("model").GetProperty("id").GetInt32());
        Assert.Equal(_refs.LocationId, created.GetProperty("location").GetProperty("id").GetInt32());
        Assert.Equal("Test demirbaşı", created.GetProperty("description").GetString());
        Assert.Equal(User, created.GetProperty("createdBy").GetString());
        Assert.False(created.GetProperty("isArchived").GetBoolean());

        var readBack = await client.GetOkAsync($"/api/assets/{id}");
        Assert.Equal(created.GetProperty("rowVersion").GetString(), readBack.GetProperty("rowVersion").GetString());
    }

    [SqlServerTheory]
    [InlineData("Faulty")]
    [InlineData("retired")]
    public async Task A_new_asset_can_start_faulty_or_retired(string status)
    {
        using var client = _api!.CreateSignedInClient(User);
        var body = _refs.NewAssetBody();
        body["status"] = status;

        using var response = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/assets", body);
        var created = await AssetApi.ReadAsync(response, HttpStatusCode.Created);

        Assert.Equal(status, created.GetProperty("status").GetString(), ignoreCase: true);
    }

    [SqlServerFact]
    public async Task Optional_fields_may_be_left_out_and_blank_serial_numbers_never_clash()
    {
        using var client = _api!.CreateSignedInClient(User);
        var first = _refs.NewAssetBody();
        first.Remove("locationId");
        first.Remove("computerName");
        first.Remove("description");
        first["serialNumber"] = "   ";
        var second = _refs.NewAssetBody();
        second.Remove("serialNumber");

        using var firstResponse = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/assets", first);
        using var secondResponse = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/assets", second);
        var created = await AssetApi.ReadAsync(firstResponse, HttpStatusCode.Created);
        await AssetApi.ReadAsync(secondResponse, HttpStatusCode.Created);

        Assert.Equal(JsonValueKind.Null, created.GetProperty("serialNumber").ValueKind);
        Assert.Equal(JsonValueKind.Null, created.GetProperty("location").ValueKind);
        Assert.Equal(JsonValueKind.Null, created.GetProperty("computerName").ValueKind);
    }

    [SqlServerFact]
    public async Task Creating_writes_a_created_audit_record_with_the_user_and_correlation_id()
    {
        using var client = _api!.CreateSignedInClient(User);
        var body = _refs.NewAssetBody();

        using var response = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/assets", body);
        var created = await AssetApi.ReadAsync(response, HttpStatusCode.Created);

        var id = created.GetProperty("id").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture);
        await using var db = fixture.CreateContext();
        var audit = await db.AuditLogs.SingleAsync(a => a.EntityName == "Asset" && a.EntityId == id);
        Assert.Equal(AuditAction.Created, audit.Action);
        Assert.Equal(User, audit.UserName);
        Assert.Equal(response.Headers.GetValues("X-Correlation-ID").Single(), audit.CorrelationId);
        Assert.Null(audit.OldValues);
        var values = JsonDocument.Parse(audit.NewValues!).RootElement;
        Assert.Equal((string)body["assetCode"]!, values.GetProperty("assetCode").GetString());
        Assert.Equal("Available", values.GetProperty("status").GetString());
        Assert.Equal(_refs.ModelName, values.GetProperty("modelName").GetString());
        Assert.Contains("Şehir-", audit.NewValues, StringComparison.Ordinal);
    }

    [SqlServerTheory]
    [InlineData("assetCode", null, "Demirbaş kodu zorunludur.")]
    [InlineData("assetCode", "   ", "Demirbaş kodu zorunludur.")]
    [InlineData("assetCode", "len:51", "Demirbaş kodu en fazla 50 karakter olabilir.")]
    [InlineData("assetCode", "DMR\u0001", "Demirbaş kodu geçersiz karakter içeriyor.")]
    [InlineData("assetType", null, "Demirbaş türü zorunludur.")]
    [InlineData("assetType", "Bilgisayar", "Demirbaş türü geçersiz.")]
    [InlineData("assetType", "2", "Demirbaş türü geçersiz.")]
    [InlineData("assetType", "Laptop, Desktop", "Demirbaş türü geçersiz.")]
    [InlineData("status", "Kayıp", "Durum geçersiz.")]
    [InlineData("status", "Assigned", "Zimmetli durumu yalnızca demirbaş zimmetlenerek verilir.")]
    [InlineData("modelId", null, "Model seçilmelidir.")]
    [InlineData("modelId", 0, "Model geçersiz.")]
    [InlineData("cityId", null, "Şehir seçilmelidir.")]
    [InlineData("departmentId", null, "Departman seçilmelidir.")]
    [InlineData("locationId", -3, "Lokasyon geçersiz.")]
    [InlineData("computerName", "len:65", "Bilgisayar adı en fazla 64 karakter olabilir.")]
    [InlineData("serialNumber", "len:101", "Seri numarası en fazla 100 karakter olabilir.")]
    [InlineData("description", "len:1001", "Açıklama en fazla 1000 karakter olabilir.")]
    public async Task Invalid_fields_are_refused_with_a_turkish_message_and_nothing_is_saved(string field, object? value, string message)
    {
        using var client = _api!.CreateSignedInClient(User);
        var body = _refs.NewAssetBody();
        var serial = (string)body["serialNumber"]!;
        body[field] = value is string text && text.StartsWith("len:", StringComparison.Ordinal)
            ? new string('x', int.Parse(text[4..], System.Globalization.CultureInfo.InvariantCulture))
            : value;

        using var response = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/assets", body);
        var problem = await AssetApi.ReadAsync(response, HttpStatusCode.BadRequest);

        Assert.Equal(message, problem.FieldError(field));
        await AssertNotSavedAsync(serial);
    }

    public static TheoryData<string, string, string> ReferenceProblems => new()
    {
        { "modelId", "unknown", AssetMessages.ModelNotFound },
        { "modelId", "inactive", AssetMessages.ModelInactive },
        { "modelId", "inactiveBrand", AssetMessages.BrandInactive },
        { "cityId", "unknown", AssetMessages.CityNotFound },
        { "cityId", "inactive", AssetMessages.CityInactive },
        { "departmentId", "unknown", AssetMessages.DepartmentNotFound },
        { "departmentId", "inactive", AssetMessages.DepartmentInactive },
        { "locationId", "unknown", AssetMessages.LocationNotFound },
        { "locationId", "inactive", AssetMessages.LocationInactive },
        { "locationId", "otherCity", AssetMessages.LocationInAnotherCity },
    };

    [SqlServerTheory]
    [MemberData(nameof(ReferenceProblems))]
    public async Task Missing_inactive_or_mismatched_lookups_are_refused(string field, string problemKind, string message)
    {
        using var client = _api!.CreateSignedInClient(User);
        var body = _refs.NewAssetBody();
        var serial = (string)body["serialNumber"]!;
        body[field] = (field, problemKind) switch
        {
            (_, "unknown") => 987_654_321,
            ("modelId", "inactive") => _refs.InactiveModelId,
            ("modelId", "inactiveBrand") => _refs.InactiveBrandModelId,
            ("cityId", "inactive") => _refs.InactiveCityId,
            ("departmentId", "inactive") => _refs.InactiveDepartmentId,
            ("locationId", "inactive") => _refs.InactiveLocationId,
            ("locationId", "otherCity") => _refs.SecondCityLocationId,
            _ => throw new ArgumentOutOfRangeException(nameof(problemKind)),
        };
        if (field == "cityId")
        {
            body.Remove("locationId");
        }

        using var response = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/assets", body);
        var problem = await AssetApi.ReadAsync(response, HttpStatusCode.BadRequest);

        Assert.Equal(message, problem.FieldError(field));
        await AssertNotSavedAsync(serial);
    }

    [SqlServerFact]
    public async Task Asset_codes_and_serial_numbers_cannot_be_reused_whatever_their_case()
    {
        using var client = _api!.CreateSignedInClient(User);
        var original = _refs.NewAssetBody();
        using (var response = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/assets", original))
        {
            await AssetApi.ReadAsync(response, HttpStatusCode.Created);
        }

        var sameCode = _refs.NewAssetBody(((string)original["assetCode"]!).ToLowerInvariant());
        var sameSerial = _refs.NewAssetBody();
        sameSerial["serialNumber"] = original["serialNumber"];
        var both = _refs.NewAssetBody((string)original["assetCode"]!);
        both["serialNumber"] = original["serialNumber"];

        using var codeResponse = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/assets", sameCode);
        using var serialResponse = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/assets", sameSerial);
        using var bothResponse = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/assets", both);
        var codeProblem = await AssetApi.ReadAsync(codeResponse, HttpStatusCode.Conflict);
        var serialProblem = await AssetApi.ReadAsync(serialResponse, HttpStatusCode.Conflict);
        var bothProblem = await AssetApi.ReadAsync(bothResponse, HttpStatusCode.Conflict);

        Assert.Equal("duplicate_value", codeProblem.GetProperty("code").GetString());
        Assert.Equal("Bu bilgiler başka bir demirbaşta kullanılıyor.", codeProblem.GetProperty("title").GetString());
        Assert.Equal(AssetMessages.AssetCodeTaken, codeProblem.FieldError("assetCode"));
        Assert.Equal(AssetMessages.SerialNumberTaken, serialProblem.FieldError("serialNumber"));
        Assert.Equal(AssetMessages.AssetCodeTaken, bothProblem.FieldError("assetCode"));
        Assert.Equal(AssetMessages.SerialNumberTaken, bothProblem.FieldError("serialNumber"));
        await AssertNotSavedAsync((string)sameSerial["serialNumber"]!, expected: 1);
    }

    [SqlServerFact]
    public async Task Serial_numbers_are_stored_without_spaces_in_upper_case_so_spacing_and_case_cannot_make_a_duplicate()
    {
        using var client = _api!.CreateSignedInClient(User);
        var serial = $"SN-I{Guid.NewGuid():N}"[..24].ToUpperInvariant();
        var body = _refs.NewAssetBody();
        body["serialNumber"] = $" {serial[..6].ToLowerInvariant()} {serial[6..]} ";

        var created = await client.CreateAssetAsync(body);

        Assert.Equal(serial, created.GetProperty("serialNumber").GetString());

        // "i" and "I" differ under the Turkish collation; the normalization makes them one number.
        var again = _refs.NewAssetBody();
        again["serialNumber"] = serial.ToLowerInvariant().Insert(4, "\u00A0");
        using var response = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/assets", again);
        Assert.Equal(AssetMessages.SerialNumberTaken, (await AssetApi.ReadAsync(response, HttpStatusCode.Conflict)).FieldError("serialNumber"));
    }

    [SqlServerFact]
    public async Task Simultaneous_creates_with_the_same_code_make_exactly_one_asset()
    {
        var code = PersistenceTestData.Unique("YARIS")[..30];
        var clients = Enumerable.Range(0, 6).Select(_ => _api!.CreateSignedInClient(User)).ToList();
        try
        {
            var responses = await Task.WhenAll(clients.Select(client => client.SendWithCsrfAsync(HttpMethod.Post, "/api/assets", _refs.NewAssetBody(code))));

            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            foreach (var conflict in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
            {
                Assert.Equal(AssetMessages.AssetCodeTaken, (await AssetApi.ReadAsync(conflict, HttpStatusCode.Conflict)).FieldError("assetCode"));
            }

            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
        finally
        {
            clients.ForEach(client => client.Dispose());
        }

        await using var db = fixture.CreateContext();
        Assert.Equal(1, await db.Assets.CountAsync(a => a.AssetCode == code));
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.EntityName == "Asset" && a.NewValues!.Contains(code)));
    }

    [SqlServerFact]
    public async Task A_create_without_the_csrf_token_is_refused()
    {
        using var client = _api!.CreateSignedInClient(User);
        var body = _refs.NewAssetBody();

        using var response = await client.PostAsync(AssetApi.Uri("/api/assets"), System.Net.Http.Json.JsonContent.Create(body));
        var problem = await AssetApi.ReadAsync(response, HttpStatusCode.BadRequest);

        Assert.Equal("csrf_invalid", problem.GetProperty("code").GetString());
        await AssertNotSavedAsync((string)body["serialNumber"]!);
    }

    [SqlServerFact]
    public async Task Only_json_bodies_are_accepted()
    {
        using var client = _api!.CreateSignedInClient(User);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["assetCode"] = "DMR-FORM" });
        using var request = new HttpRequestMessage(HttpMethod.Post, AssetApi.Uri("/api/assets")) { Content = form };
        request.Headers.Add(AuthClient.CsrfHeader, await client.GetCsrfTokenAsync());

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    private async Task AssertNotSavedAsync(string serialNumber, int expected = 0)
    {
        await using var db = fixture.CreateContext();
        Assert.Equal(expected, await db.Assets.CountAsync(a => a.SerialNumber == serialNumber));
    }
}
