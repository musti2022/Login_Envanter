using System.Net;
using System.Text.Json;
using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Domain.Catalog;
using EnterpriseInventory.Domain.Employees;
using EnterpriseInventory.Domain.Organization;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Persistence;

namespace EnterpriseInventory.IntegrationTests.Assets;

/// <summary>
/// <c>GET /api/assets</c> and <c>GET /api/assets/{id}</c> against a database of their own, so the tests know
/// every asset in it: seven active ones (DMR-0001 to DMR-0007, saved out of order) and one archived.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class AssetListTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime, IDisposable
{
    private static readonly string[] ActiveCodes = ["DMR-0001", "DMR-0002", "DMR-0003", "DMR-0004", "DMR-0005", "DMR-0006", "DMR-0007"];

    private TestApiFactory? _api;
    private int _assignedAssetId;
    private int _archivedAssetId;
    private DateTimeOffset _assignedAt;

    public async Task InitializeAsync()
    {
        if (SqlServerDatabaseFixture.ServerConnectionString is null)
        {
            return;
        }

        // Read-only, so one seeded database serves every test in the class.
        (var connectionString, _assignedAssetId, _archivedAssetId, _assignedAt) = await fixture.SharedAsync(nameof(AssetListTests), SeedAsync);
        _api = new TestApiFactory(connectionString, settings: new Dictionary<string, string?> { ["RateLimiting:PermitLimit"] = "1000" });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _api?.Dispose();

    private async Task<(string ConnectionString, int AssignedAssetId, int ArchivedAssetId, DateTimeOffset AssignedAt)> SeedAsync()
    {
        var connectionString = await fixture.CreateMigratedDatabaseAsync();
        var dell = Brand.Create("Dell");
        var latitude = AssetModel.Create(dell, "Latitude 5440");
        var hp = AssetModel.Create(Brand.Create("HP"), "EliteBook 840");
        var istanbul = City.Create("İstanbul");
        var office = Location.Create(istanbul, "Merkez Ofis");
        var ankara = City.Create("Ankara");
        var it = Department.Create("Bilgi İşlem");
        var accounting = Department.Create("Muhasebe");

        Asset New(string code, AssetModel model, City city, Department department, Location? location = null) =>
            Asset.Create(code, AssetType.Laptop, model, city, department, location, computerName: $"PC-{code}", serialNumber: $"SN-{code}");

        // Saved out of code order, so a list in code order is not just insertion order.
        var assets = new[]
        {
            New("DMR-0005", hp, ankara, accounting),
            New("DMR-0002", latitude, istanbul, it, office),
            New("DMR-0007", hp, ankara, it),
            New("DMR-0001", latitude, istanbul, it, office),
            New("DMR-0003", latitude, istanbul, it, office),
            New("DMR-0006", latitude, istanbul, accounting),
            New("DMR-0004", hp, ankara, accounting),
        };
        var assigned = assets[4];
        var assignedAt = fixture.Clock.GetUtcNow();
        var employee = Employee.Create(Guid.NewGuid(), "ayse.yilmaz", "Ayşe Yılmaz", null, "Muhasebe", null, isActive: true, assignedAt);
        assigned.Assign(employee, "Dizüstü + çanta", notes: null, assignedBy: "mehmet.admin", assignedAt);

        var archived = New("DMR-0000", hp, ankara, it);
        archived.Archive();

        await using (var context = fixture.CreateContextFor(connectionString))
        {
            context.AddRange(assets);
            context.Add(archived);
            await context.SaveChangesAsync();
        }

        return (connectionString, assigned.Id, archived.Id, assignedAt);
    }

    [SqlServerFact]
    public async Task The_first_page_lists_the_active_assets_in_code_order_with_totals()
    {
        using var client = _api!.CreateSignedInClient();

        var page = await client.GetOkAsync("/api/assets");

        Assert.Equal(ActiveCodes, page.Codes());
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(25, page.GetProperty("pageSize").GetInt32());
        Assert.Equal(7, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, page.GetProperty("totalPages").GetInt32());
    }

    [SqlServerFact]
    public async Task Pages_split_the_list_without_gaps_or_repeats()
    {
        using var client = _api!.CreateSignedInClient();

        var pages = new List<JsonElement>();
        for (var number = 1; number <= 3; number++)
        {
            pages.Add(await client.GetOkAsync($"/api/assets?page={number}&pageSize=3"));
        }

        Assert.Equal([3, 3, 1], pages.Select(p => p.GetProperty("items").GetArrayLength()));
        Assert.Equal(ActiveCodes, pages.SelectMany(p => p.Codes()));
        Assert.All(pages, p => Assert.Equal(7, p.GetProperty("totalCount").GetInt32()));
        Assert.All(pages, p => Assert.Equal(3, p.GetProperty("totalPages").GetInt32()));
    }

    [SqlServerFact]
    public async Task A_page_past_the_end_is_empty_but_keeps_the_totals()
    {
        using var client = _api!.CreateSignedInClient();

        var page = await client.GetOkAsync("/api/assets?page=4&pageSize=3");

        Assert.Empty(page.Codes());
        Assert.Equal(4, page.GetProperty("page").GetInt32());
        Assert.Equal(7, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, page.GetProperty("totalPages").GetInt32());
    }

    [SqlServerFact]
    public async Task The_largest_page_size_is_100()
    {
        using var client = _api!.CreateSignedInClient();

        var page = await client.GetOkAsync("/api/assets?pageSize=100");

        Assert.Equal(100, page.GetProperty("pageSize").GetInt32());
        Assert.Equal(ActiveCodes, page.Codes());
    }

    [SqlServerTheory]
    [InlineData("page=0", "page", "Sayfa numarası 1 ile 100000 arasında olmalıdır.")]
    [InlineData("page=-1", "page", "Sayfa numarası 1 ile 100000 arasında olmalıdır.")]
    [InlineData("page=100001", "page", "Sayfa numarası 1 ile 100000 arasında olmalıdır.")]
    [InlineData("pageSize=0", "pageSize", "Sayfa boyutu 1 ile 100 arasında olmalıdır.")]
    [InlineData("pageSize=101", "pageSize", "Sayfa boyutu 1 ile 100 arasında olmalıdır.")]
    public async Task Paging_outside_the_limits_is_refused_with_a_turkish_field_error(string query, string field, string message)
    {
        using var client = _api!.CreateSignedInClient();

        using var response = await client.GetAsync(AssetApi.Uri($"/api/assets?{query}"));
        var problem = await AssetApi.ReadAsync(response, HttpStatusCode.BadRequest);

        Assert.Equal("İstek geçersiz.", problem.GetProperty("title").GetString());
        Assert.Equal(message, problem.FieldError(field));
    }

    [SqlServerFact]
    public async Task Paging_that_is_not_a_number_is_refused()
    {
        using var client = _api!.CreateSignedInClient();

        using var response = await client.GetAsync(AssetApi.Uri("/api/assets?page=iki"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await ProblemJson.ReadAsync(response);
    }

    [SqlServerFact]
    public async Task Rows_carry_the_table_columns_and_the_current_holder()
    {
        using var client = _api!.CreateSignedInClient();

        var page = await client.GetOkAsync("/api/assets");
        var assigned = page.Item("DMR-0003");
        var free = page.Item("DMR-0005");

        Assert.Equal(_assignedAssetId, assigned.GetProperty("id").GetInt32());
        Assert.Equal("PC-DMR-0003", assigned.GetProperty("computerName").GetString());
        Assert.Equal("Dell", assigned.GetProperty("brandName").GetString());
        Assert.Equal("Latitude 5440", assigned.GetProperty("modelName").GetString());
        Assert.Equal("SN-DMR-0003", assigned.GetProperty("serialNumber").GetString());
        Assert.Equal("Laptop", assigned.GetProperty("assetType").GetString());
        Assert.Equal("Assigned", assigned.GetProperty("status").GetString());
        Assert.Equal("İstanbul", assigned.GetProperty("cityName").GetString());
        Assert.Equal("Bilgi İşlem", assigned.GetProperty("departmentName").GetString());
        Assert.Equal("Merkez Ofis", assigned.GetProperty("locationName").GetString());
        Assert.Equal("ayse.yilmaz", assigned.GetProperty("assignedUserName").GetString());
        Assert.Equal("Ayşe Yılmaz", assigned.GetProperty("assignedDisplayName").GetString());
        Assert.Equal("Dizüstü + çanta", assigned.GetProperty("assignmentDescription").GetString());
        Assert.False(assigned.GetProperty("isArchived").GetBoolean());

        Assert.Equal("Available", free.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, free.GetProperty("locationName").ValueKind);
        Assert.Equal(JsonValueKind.Null, free.GetProperty("assignedUserName").ValueKind);
        Assert.Equal(JsonValueKind.Null, free.GetProperty("assignmentDescription").ValueKind);
    }

    [SqlServerFact]
    public async Task Details_show_every_field_the_holder_and_the_row_version()
    {
        using var client = _api!.CreateSignedInClient();

        var asset = await client.GetOkAsync($"/api/assets/{_assignedAssetId}");

        Assert.Equal("DMR-0003", asset.GetProperty("assetCode").GetString());
        Assert.Equal("Latitude 5440", asset.GetProperty("model").GetProperty("name").GetString());
        Assert.Equal("Dell", asset.GetProperty("brand").GetProperty("name").GetString());
        Assert.Equal("İstanbul", asset.GetProperty("city").GetProperty("name").GetString());
        Assert.Equal("Merkez Ofis", asset.GetProperty("location").GetProperty("name").GetString());
        Assert.Equal("Bilgi İşlem", asset.GetProperty("department").GetProperty("name").GetString());
        Assert.Equal("Assigned", asset.GetProperty("status").GetString());
        Assert.Equal(SqlServerDatabaseFixture.DefaultUser, asset.GetProperty("createdBy").GetString());

        var holder = asset.GetProperty("activeAssignment");
        Assert.Equal("ayse.yilmaz", holder.GetProperty("userName").GetString());
        Assert.Equal("Ayşe Yılmaz", holder.GetProperty("displayName").GetString());
        Assert.Equal("mehmet.admin", holder.GetProperty("assignedBy").GetString());
        Assert.Equal(_assignedAt, holder.GetProperty("assignedAt").GetDateTimeOffset());

        Assert.Equal(8, Convert.FromBase64String(asset.GetProperty("rowVersion").GetString()!).Length);
    }

    [SqlServerFact]
    public async Task An_unknown_asset_is_404_with_a_turkish_message()
    {
        using var client = _api!.CreateSignedInClient();

        using var response = await client.GetAsync(AssetApi.Uri("/api/assets/987654"));
        var problem = await AssetApi.ReadAsync(response, HttpStatusCode.NotFound);

        Assert.Equal("Demirbaş bulunamadı.", problem.GetProperty("title").GetString());
        Assert.Equal("asset_not_found", problem.GetProperty("code").GetString());
    }

    [SqlServerFact]
    public async Task Archived_assets_are_left_out_of_the_list_but_their_details_stay_readable()
    {
        using var client = _api!.CreateSignedInClient();

        var page = await client.GetOkAsync("/api/assets");
        var archived = await client.GetOkAsync($"/api/assets/{_archivedAssetId}");

        Assert.DoesNotContain("DMR-0000", page.Codes());
        Assert.Equal("DMR-0000", archived.GetProperty("assetCode").GetString());
        Assert.True(archived.GetProperty("isArchived").GetBoolean());
    }
}
