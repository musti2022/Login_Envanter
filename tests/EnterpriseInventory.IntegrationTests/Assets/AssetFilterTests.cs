using System.Net;
using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Domain.Catalog;
using EnterpriseInventory.Domain.Employees;
using EnterpriseInventory.Domain.Organization;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Persistence;

namespace EnterpriseInventory.IntegrationTests.Assets;

/// <summary>
/// Search, filters and sorting of <c>GET /api/assets</c>, against a database of their own so every expected list
/// is exact. Six active assets (A-001 to A-006) and two archived ones (A-000, A-008):
/// <code>
/// A-001 Laptop  Dell Latitude 5440 İstanbul Bilgi İşlem       Merkez Ofis PC-IST-01  SN-100 Available
/// A-002 Laptop  Dell Latitude 5440 İstanbul Muhasebe          Merkez Ofis PC-IST-02  SN-200 Assigned: ali.kaya, Ali Kaya, "Dizüstü + çanta"
/// A-003 Desktop Dell OptiPlex 7010 İzmir    Bilgi İşlem       Alsancak    PC-IZM-01  SN-300 Faulty
/// A-004 Monitor HP EliteDisplay    Ankara   İnsan Kaynakları  -           -          SN-400 Retired
/// A-005 Laptop  HP EliteBook 840   Ankara   Bilgi İşlem       -           PC-ANK-01  SN-500 Available
/// A-006 Desktop Dell OptiPlex 7010 İstanbul İnsan Kaynakları  Depo        PC-IST-03  -      Assigned: zeynep.demir, Zeynep Demir, "Masaüstü %100_hazır"
/// A-000 Laptop  Dell Latitude 5440 İzmir    Muhasebe          Alsancak    (archived)
/// A-008 Desktop HP EliteBook 840   Ankara   Bilgi İşlem       -           (archived)
/// </code>
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class AssetFilterTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime, IDisposable
{
    private TestApiFactory? _api;
    private Dictionary<string, int> _ids = [];

    public async Task InitializeAsync()
    {
        if (SqlServerDatabaseFixture.ServerConnectionString is null)
        {
            return;
        }

        // Read-only, so one seeded database serves every test in the class.
        (var connectionString, _ids) = await fixture.SharedAsync(nameof(AssetFilterTests), SeedAsync);
        _api = new TestApiFactory(connectionString, settings: new Dictionary<string, string?> { ["RateLimiting:PermitLimit"] = "1000" });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _api?.Dispose();

    [SqlServerTheory]
    [InlineData("çanta", "A-002")]
    [InlineData("KAYA", "A-002")]
    [InlineData("zeynep.demir", "A-006")]
    [InlineData("optiplex", "A-003 A-006")]
    [InlineData("DELL", "A-001 A-002 A-003 A-006")]
    [InlineData("İZMİR", "A-003")]
    [InlineData("bilgi işlem", "A-001 A-003 A-005")]
    [InlineData("dell izmir", "A-003")]
    [InlineData("  merkez   ofis ", "A-001 A-002")]
    [InlineData("sn-1", "A-001")]
    [InlineData("pc-ist", "A-001 A-002 A-006")]
    [InlineData("%", "A-006")]
    [InlineData("_", "A-006")]
    [InlineData("100_", "A-006")]
    [InlineData("%10", "A-006")]
    [InlineData("[a]", "")]
    [InlineData("canta", "A-002")]
    [InlineData("ISTANBUL", "A-001 A-002 A-006")]
    [InlineData("ınsan", "A-004 A-006")]
    [InlineData("HAZIR", "A-006")]
    [InlineData("böyle-bir-şey-yok", "")]
    public async Task Search_finds_every_word_in_the_table_columns_ignoring_case_and_accents(string search, string expected)
    {
        using var client = _api!.CreateSignedInClient();

        var page = await client.GetOkAsync($"/api/assets?search={Uri.EscapeDataString(search)}");

        Assert.Equal(Codes(expected), page.Codes());
        Assert.Equal(Codes(expected).Length, page.GetProperty("totalCount").GetInt32());
    }

    [SqlServerTheory]
    [InlineData("status=Faulty&status=Retired", "A-003 A-004")]
    [InlineData("status=assigned", "A-002 A-006")]
    [InlineData("status=Assigned&status=assigned", "A-002 A-006")]
    [InlineData("assetType=Desktop", "A-003 A-006")]
    [InlineData("assetType=Desktop&assetType=Monitor", "A-003 A-004 A-006")]
    [InlineData("brandId={HP}", "A-004 A-005")]
    [InlineData("modelId={OptiPlex}", "A-003 A-006")]
    [InlineData("cityId={İstanbul}", "A-001 A-002 A-006")]
    [InlineData("locationId={Depo}", "A-006")]
    [InlineData("departmentId={Bilgi İşlem}", "A-001 A-003 A-005")]
    [InlineData("cityId={İstanbul}&status=Available", "A-001")]
    [InlineData("brandId={Dell}&assetType=Laptop&search=ofis", "A-001 A-002")]
    [InlineData("archived=false", "A-001 A-002 A-003 A-004 A-005 A-006")]
    [InlineData("archived=true", "A-000 A-008")]
    [InlineData("archived=true&search=izmir", "A-000")]
    [InlineData("archived=true&brandId={HP}", "A-008")]
    [InlineData("brandId=987654", "")]
    public async Task Filters_narrow_the_list_and_combine(string query, string expected)
    {
        using var client = _api!.CreateSignedInClient();

        var page = await client.GetOkAsync($"/api/assets?{WithIds(query)}");

        Assert.Equal(Codes(expected), page.Codes());
    }

    [SqlServerFact]
    public async Task Totals_and_pages_count_only_the_matching_assets()
    {
        using var client = _api!.CreateSignedInClient();

        var first = await client.GetOkAsync($"/api/assets?{WithIds("departmentId={Bilgi İşlem}")}&pageSize=2");
        var second = await client.GetOkAsync($"/api/assets?{WithIds("departmentId={Bilgi İşlem}")}&pageSize=2&page=2");

        Assert.Equal(["A-001", "A-003"], first.Codes());
        Assert.Equal(["A-005"], second.Codes());
        Assert.Equal(3, first.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, first.GetProperty("totalPages").GetInt32());
    }

    [SqlServerTheory]
    [InlineData("", "A-001 A-002 A-003 A-004 A-005 A-006")]
    [InlineData("sortDirection=desc", "A-006 A-005 A-004 A-003 A-002 A-001")]
    [InlineData("sortBy=computerName", "A-004 A-005 A-001 A-002 A-006 A-003")]
    [InlineData("sortBy=COMPUTERNAME&sortDirection=DESC", "A-003 A-006 A-002 A-001 A-005 A-004")]
    [InlineData("sortBy=cityName", "A-004 A-005 A-001 A-002 A-006 A-003")]
    [InlineData("sortBy=departmentName", "A-001 A-003 A-005 A-004 A-006 A-002")]
    [InlineData("sortBy=locationName", "A-004 A-005 A-003 A-006 A-001 A-002")]
    [InlineData("sortBy=brandName&sortDirection=desc", "A-005 A-004 A-006 A-003 A-002 A-001")]
    [InlineData("sortBy=modelName", "A-005 A-004 A-001 A-002 A-003 A-006")]
    [InlineData("sortBy=serialNumber", "A-006 A-001 A-002 A-003 A-004 A-005")]
    [InlineData("sortBy=status", "A-001 A-005 A-002 A-006 A-003 A-004")]
    [InlineData("sortBy=assetType", "A-003 A-006 A-001 A-002 A-005 A-004")]
    [InlineData("sortBy=assignedDisplayName&sortDirection=desc", "A-006 A-002 A-005 A-004 A-003 A-001")]
    [InlineData("sortBy=assignedUserName", "A-001 A-003 A-004 A-005 A-002 A-006")]
    public async Task The_list_can_be_sorted_by_any_column(string query, string expected)
    {
        using var client = _api!.CreateSignedInClient();

        var page = await client.GetOkAsync($"/api/assets?{query}");

        Assert.Equal(Codes(expected), page.Codes());
    }

    [SqlServerFact]
    public async Task Sorted_pages_split_the_list_without_gaps_or_repeats()
    {
        using var client = _api!.CreateSignedInClient();

        var pages = new List<string>();
        for (var number = 1; number <= 3; number++)
        {
            pages.AddRange((await client.GetOkAsync($"/api/assets?sortBy=cityName&pageSize=2&page={number}")).Codes());
        }

        Assert.Equal(["A-004", "A-005", "A-001", "A-002", "A-006", "A-003"], pages);
    }

    [SqlServerTheory]
    [InlineData("search=len:101", "search", "Arama metni en fazla 100 karakter olabilir.")]
    [InlineData("search=bir%20iki%20%C3%BC%C3%A7%20d%C3%B6rt%20be%C5%9F%20alt%C4%B1", "search", "Arama en fazla 5 kelime içerebilir.")]
    [InlineData("search=a%00b", "search", "Arama metni geçersiz karakter içeriyor.")]
    [InlineData("status=Kay%C4%B1p", "status", "Durum filtresi geçersiz. Geçerli değerler: Available, Assigned, Faulty, Retired.")]
    [InlineData("status=Available&status=2", "status", "Durum filtresi geçersiz. Geçerli değerler: Available, Assigned, Faulty, Retired.")]
    [InlineData("assetType=Bilgisayar", "assetType", "Demirbaş türü filtresi geçersiz. Geçerli değerler: Desktop, Laptop, Monitor, Printer, Phone, Tablet, Server, NetworkDevice, Peripheral, Other.")]
    [InlineData("brandId=0", "brandId", "Marka filtresi geçersiz.")]
    [InlineData("modelId=-1", "modelId", "Model filtresi geçersiz.")]
    [InlineData("cityId=0", "cityId", "Şehir filtresi geçersiz.")]
    [InlineData("departmentId=0", "departmentId", "Departman filtresi geçersiz.")]
    [InlineData("locationId=0", "locationId", "Lokasyon filtresi geçersiz.")]
    [InlineData("sortBy=rowVersion", "sortBy", "Sıralama alanı geçersiz. Geçerli alanlar: assetCode, computerName, serialNumber, brandName, modelName, assetType, status, cityName, departmentName, locationName, assignedUserName, assignedDisplayName, createdAt, updatedAt.")]
    [InlineData("sortBy=", "sortBy", "Sıralama alanı geçersiz. Geçerli alanlar: assetCode, computerName, serialNumber, brandName, modelName, assetType, status, cityName, departmentName, locationName, assignedUserName, assignedDisplayName, createdAt, updatedAt.")]
    [InlineData("sortDirection=yukari", "sortDirection", "Sıralama yönü 'asc' veya 'desc' olmalıdır.")]
    public async Task Invalid_filters_are_refused_with_a_turkish_field_error(string query, string field, string message)
    {
        using var client = _api!.CreateSignedInClient();
        if (query.StartsWith("search=len:", StringComparison.Ordinal))
        {
            query = "search=" + new string('x', int.Parse(query["search=len:".Length..], System.Globalization.CultureInfo.InvariantCulture));
        }

        using var response = await client.GetAsync(AssetApi.Uri($"/api/assets?{query}"));
        var problem = await AssetApi.ReadAsync(response, HttpStatusCode.BadRequest);

        Assert.Equal(message, problem.FieldError(field));
    }

    [SqlServerTheory]
    [InlineData("archived=evet")]
    [InlineData("brandId=dell")]
    public async Task Filters_of_the_wrong_type_are_refused(string query)
    {
        using var client = _api!.CreateSignedInClient();

        using var response = await client.GetAsync(AssetApi.Uri($"/api/assets?{query}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await ProblemJson.ReadAsync(response);
    }

    private async Task<(string ConnectionString, Dictionary<string, int> Ids)> SeedAsync()
    {
        var connectionString = await fixture.CreateMigratedDatabaseAsync();
        var dell = Brand.Create("Dell");
        var hp = Brand.Create("HP");
        var latitude = AssetModel.Create(dell, "Latitude 5440");
        var optiplex = AssetModel.Create(dell, "OptiPlex 7010");
        var elitebook = AssetModel.Create(hp, "EliteBook 840");
        var display = AssetModel.Create(hp, "EliteDisplay");
        var istanbul = City.Create("İstanbul");
        var izmir = City.Create("İzmir");
        var ankara = City.Create("Ankara");
        var office = Location.Create(istanbul, "Merkez Ofis");
        var depot = Location.Create(istanbul, "Depo");
        var alsancak = Location.Create(izmir, "Alsancak");
        var it = Department.Create("Bilgi İşlem");
        var accounting = Department.Create("Muhasebe");
        var hr = Department.Create("İnsan Kaynakları");
        var now = fixture.Clock.GetUtcNow();

        var a1 = Asset.Create("A-001", AssetType.Laptop, latitude, istanbul, it, office, "PC-IST-01", "SN-100");
        var a2 = Asset.Create("A-002", AssetType.Laptop, latitude, istanbul, accounting, office, "PC-IST-02", "SN-200");
        a2.Assign(Employee.Create(Guid.NewGuid(), "ali.kaya", "Ali Kaya", null, "Muhasebe", null, isActive: true, now), "Dizüstü + çanta", null, "mehmet.admin", now);
        var a3 = Asset.Create("A-003", AssetType.Desktop, optiplex, izmir, it, alsancak, "PC-IZM-01", "SN-300");
        a3.ChangeStatus(AssetStatus.Faulty);
        var a4 = Asset.Create("A-004", AssetType.Monitor, display, ankara, hr, serialNumber: "SN-400");
        a4.ChangeStatus(AssetStatus.Retired);
        var a5 = Asset.Create("A-005", AssetType.Laptop, elitebook, ankara, it, computerName: "PC-ANK-01", serialNumber: "SN-500");
        var a6 = Asset.Create("A-006", AssetType.Desktop, optiplex, istanbul, hr, depot, "PC-IST-03");
        a6.Assign(Employee.Create(Guid.NewGuid(), "zeynep.demir", "Zeynep Demir", null, "İK", null, isActive: true, now), "Masaüstü %100_hazır", null, "mehmet.admin", now);
        var a0 = Asset.Create("A-000", AssetType.Laptop, latitude, izmir, accounting, alsancak, "PC-IZM-00", "SN-000");
        a0.Archive();
        var a8 = Asset.Create("A-008", AssetType.Desktop, elitebook, ankara, it, computerName: "PC-ANK-08");
        a8.Archive();

        // Saved out of code order, so a list in code order is not just insertion order.
        Asset[] assets = [a5, a2, a0, a6, a1, a8, a4, a3];
        await using (var context = fixture.CreateContextFor(connectionString))
        {
            context.AddRange(assets);
            await context.SaveChangesAsync();
        }

        var ids = new Dictionary<string, int>
        {
            ["Dell"] = dell.Id,
            ["HP"] = hp.Id,
            ["OptiPlex"] = optiplex.Id,
            ["İstanbul"] = istanbul.Id,
            ["Depo"] = depot.Id,
            ["Bilgi İşlem"] = it.Id,
        };
        return (connectionString, ids);
    }

    private static string[] Codes(string expected) => expected.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Replaces <c>{name}</c> with the seeded ID of that lookup.</summary>
    private string WithIds(string query) =>
        _ids.Aggregate(query, (current, id) => current.Replace($"{{{id.Key}}}", id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
}
