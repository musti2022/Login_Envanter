using System.ComponentModel.DataAnnotations;
using EnterpriseInventory.Application;
using EnterpriseInventory.Application.Abstractions;
using EnterpriseInventory.Application.Assets;
using EnterpriseInventory.Application.Exports;
using EnterpriseInventory.Domain.Assets;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EnterpriseInventory.UnitTests.Assets;

public class AssetExportServiceTests
{
    /// <summary>Late on 9 October in UTC, already 10 October in Istanbul (UTC+3).</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 22, 30, 0, TimeSpan.Zero);

    private static readonly IValidator<AssetListRequest> ListValidator =
        new ServiceCollection().AddApplication().BuildServiceProvider().GetRequiredService<IValidator<AssetListRequest>>();

    private static AssetListItem Item(string code, AssetStatus status = AssetStatus.Available, string? holder = null) => new(
        Id: code.Length,
        AssetCode: code,
        ComputerName: "PC-" + code,
        BrandName: "Dell",
        ModelName: "Latitude 5440",
        SerialNumber: "SN-" + code,
        AssetType: AssetType.Laptop,
        Status: status,
        CityName: "İstanbul",
        DepartmentName: "Bilgi İşlem",
        LocationName: holder is null ? null : "Kat 3",
        AssignedUserName: holder,
        AssignedDisplayName: holder is null ? null : "Ayşe Yılmaz",
        AssignmentDescription: holder is null ? null : "Saha dizüstüsü",
        IsArchived: false,
        CreatedAt: new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero),
        UpdatedAt: holder is null ? null : new DateTimeOffset(2026, 10, 9, 21, 15, 0, TimeSpan.Zero));

    private static (AssetExportService Service, FakeStore Store) Create(FakeStore? store = null, ReportingOptions? options = null)
    {
        store ??= new FakeStore();
        var service = new AssetExportService(
            ListValidator,
            store,
            new SignedIn("ayse.admin"),
            new FixedTime(Now),
            Options.Create(options ?? new ReportingOptions()),
            NullLogger<AssetExportService>.Instance);
        return (service, store);
    }

    [Fact]
    public async Task Every_matching_asset_is_written_with_Turkish_headers_and_labels_and_Istanbul_times()
    {
        var (service, store) = Create(new FakeStore { Items = [Item("PC-001", AssetStatus.Assigned, "ayse.yilmaz"), Item("PC-002")] });

        var result = await service.ExportAsync(new AssetListRequest(), CancellationToken.None);

        Assert.Equal(AssetExportOutcome.Succeeded, result.Outcome);
        Assert.Equal("envanter-2026-10-10", result.FileName);
        var list = result.Spreadsheet!.Sheets[0];
        Assert.Equal("Envanter", list.Name);
        Assert.True(list.Filterable);
        Assert.Equal(
            [
                "Demirbaş Kodu", "Kullanıcı Adı", "Ad Soyad", "Bilgisayar Adı", "Marka", "Model", "Seri No", "Zimmet Tanımı", "Şehir",
                "Lokasyon", "Departman", "Tür", "Durum", "Eklenme Zamanı", "Son Değişiklik",
            ],
            list.Columns.Select(c => c.Header));
        Assert.Equal(
            [
                "PC-001", "ayse.yilmaz", "Ayşe Yılmaz", "PC-PC-001", "Dell", "Latitude 5440", "SN-PC-001", "Saha dizüstüsü", "İstanbul",
                "Kat 3", "Bilgi İşlem", "Dizüstü", "Zimmetli", new DateTime(2026, 3, 1, 12, 0, 0), new DateTime(2026, 10, 10, 0, 15, 0),
            ],
            list.Rows[0]);

        // Nobody holds the second asset and it was never changed: those cells stay empty.
        Assert.Equal(["PC-002", null, null], list.Rows[1].Take(3));
        Assert.Equal(["Boşta", new DateTime(2026, 3, 1, 12, 0, 0), null], list.Rows[1].Skip(12));
        Assert.Equal(50_000, store.MaxRows);
    }

    [Fact]
    public async Task The_info_sheet_says_what_was_exported_by_whom_when_and_how_it_was_filtered_and_sorted()
    {
        var (service, _) = Create(new FakeStore
        {
            Items = [Item("PC-001")],
            Names = new AssetFilterNames("Dell", null, null, "Bilgi İşlem", null),
        });

        var result = await service.ExportAsync(
            new AssetListRequest
            {
                Search = "  pc   ist ",
                Status = ["Faulty", "retired"],
                AssetType = ["Laptop"],
                BrandId = 7,
                ModelId = 9,
                DepartmentId = 3,
                SortBy = "computerName",
                SortDirection = "desc",
            },
            CancellationToken.None);

        var info = result.Spreadsheet!.Sheets[1];
        Assert.Equal("Bilgi", info.Name);
        Assert.Equal(
            [
                ["Rapor", "Envanter listesi"],
                ["Oluşturan", "ayse.admin"],
                ["Oluşturulma zamanı", new DateTime(2026, 10, 10, 1, 30, 0)],
                ["Saat dilimi", "Europe/Istanbul (UTC+03:00)"],
                ["Demirbaş sayısı", 1],
                ["Arama", "pc ist"],
                ["Durum", "Arızalı, Hurda"],
                ["Tür", "Dizüstü"],
                ["Marka", "Dell"],
                ["Model", "Kimlik 9 (kayıt bulunamadı)"],
                ["Şehir", "Tümü"],
                ["Departman", "Bilgi İşlem"],
                ["Lokasyon", "Tümü"],
                ["Sıralama", "Bilgisayar Adı, azalan"],
            ],
            info.Rows);
    }

    [Fact]
    public async Task The_list_is_read_with_the_filters_and_order_of_the_inventory_list_and_paging_is_ignored()
    {
        var (service, store) = Create();

        await service.ExportAsync(
            new AssetListRequest { Page = 3, PageSize = 10, CityId = 4, Status = ["Available"], SortBy = "updatedAt" },
            CancellationToken.None);

        var criteria = store.Criteria!;
        Assert.Equal(4, criteria.CityId);
        Assert.Equal([AssetStatus.Available], criteria.Statuses);
        Assert.Equal(AssetSortField.UpdatedAt, criteria.SortBy);
        Assert.False(criteria.Descending);
    }

    [Fact]
    public async Task Archived_assets_are_exported_to_a_file_named_for_the_archive()
    {
        var (service, _) = Create();

        var result = await service.ExportAsync(new AssetListRequest { Archived = true }, CancellationToken.None);

        Assert.Equal("envanter-arsiv-2026-10-10", result.FileName);
        Assert.Equal(["Rapor", "Arşivlenmiş demirbaşlar"], result.Spreadsheet!.Sheets[1].Rows[0]);
        Assert.Equal(["Demirbaş sayısı", 0], result.Spreadsheet.Sheets[1].Rows[4]);
    }

    [Fact]
    public async Task More_assets_than_allowed_are_refused_without_reading_them()
    {
        var (service, store) = Create(new FakeStore { MatchCount = 12 }, new ReportingOptions { MaxExportRows = 10 });

        var result = await service.ExportAsync(new AssetListRequest(), CancellationToken.None);

        Assert.Equal(AssetExportOutcome.TooManyRows, result.Outcome);
        Assert.Equal((12, 10), (result.MatchCount, result.MaxRows));
        Assert.Null(result.Spreadsheet);
        Assert.Equal(10, store.MaxRows);
        Assert.False(store.NamesRead);
    }

    [Fact]
    public async Task Filters_the_list_would_refuse_are_refused_before_reading_anything()
    {
        var (service, store) = Create();

        var result = await service.ExportAsync(new AssetListRequest { Status = ["Bozuk"], BrandId = 0 }, CancellationToken.None);

        Assert.Equal(AssetExportOutcome.Invalid, result.Outcome);
        Assert.Contains("Status", result.Errors!.Keys);
        Assert.Contains("BrandId", result.Errors.Keys);
        Assert.Null(store.Criteria);
    }

    [Fact]
    public async Task Dates_follow_the_configured_time_zone()
    {
        var (service, _) = Create(new FakeStore { Items = [Item("PC-001")] }, new ReportingOptions { TimeZone = "America/New_York" });

        var result = await service.ExportAsync(new AssetListRequest(), CancellationToken.None);

        // 22:30 UTC on 9 October is 18:30 the same day in New York (UTC-4 in October).
        Assert.Equal("envanter-2026-10-09", result.FileName);
        Assert.Equal(new DateTime(2026, 3, 1, 4, 0, 0), result.Spreadsheet!.Sheets[0].Rows[0][13]);
        Assert.Equal(["Saat dilimi", "America/New_York (UTC-04:00)"], result.Spreadsheet.Sheets[1].Rows[3]);
    }

    [Theory]
    [InlineData("Europe/Istanbul", true)]
    [InlineData("UTC", true)]
    [InlineData("Mars/Olympus", false)]
    [InlineData("", false)]
    public void An_unknown_time_zone_is_refused_at_startup(string timeZone, bool valid)
    {
        var options = new ReportingOptions { TimeZone = timeZone };

        Assert.Equal(valid, Validator.TryValidateObject(options, new ValidationContext(options), [], validateAllProperties: true));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(1_000_001, false)]
    public void The_row_limit_must_be_sensible(int maxRows, bool valid)
    {
        var options = new ReportingOptions { MaxExportRows = maxRows };

        Assert.Equal(valid, Validator.TryValidateObject(options, new ValidationContext(options), [], validateAllProperties: true));
    }

    [Fact]
    public void Statuses_types_and_sort_columns_read_the_same_as_on_screen()
    {
        // The words of the web app's labels.ts and inventory columns.
        Assert.Equal(
            ["Boşta", "Zimmetli", "Arızalı", "Hurda"],
            Enum.GetValues<AssetStatus>().Select(AssetLabels.Of));
        Assert.Equal(
            ["Masaüstü", "Dizüstü", "Monitör", "Yazıcı", "Telefon", "Tablet", "Sunucu", "Ağ cihazı", "Çevre birimi", "Diğer"],
            Enum.GetValues<AssetType>().Select(AssetLabels.Of));
        Assert.All(Enum.GetValues<AssetSortField>(), field => Assert.NotEqual(field.ToString(), AssetLabels.Of(field)));
    }

    private sealed class SignedIn(string userName) : ICurrentUser
    {
        public string? UserName => userName;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeStore : IAssetStore
    {
        public IReadOnlyList<AssetListItem> Items { get; init; } = [];

        /// <summary>When set, more assets match than the rows given.</summary>
        public int? MatchCount { get; init; }

        public AssetFilterNames Names { get; init; } = new(null, null, null, null, null);

        public AssetListCriteria? Criteria { get; private set; }

        public int? MaxRows { get; private set; }

        public bool NamesRead { get; private set; }

        public Task<AssetExportRows> ExportAsync(AssetListCriteria criteria, int maxRows, CancellationToken cancellationToken)
        {
            Criteria = criteria;
            MaxRows = maxRows;
            var count = MatchCount ?? Items.Count;
            return Task.FromResult(count > maxRows ? new AssetExportRows(count, []) : new AssetExportRows(count, Items));
        }

        public Task<AssetFilterNames> FilterNamesAsync(AssetListCriteria criteria, CancellationToken cancellationToken)
        {
            NamesRead = true;
            return Task.FromResult(Names);
        }

        public Task<PagedResult<AssetListItem>> ListAsync(AssetListCriteria criteria, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AssetDetails?> FindAsync(int id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AssetWriteResult> CreateAsync(AssetDraft draft, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AssetWriteResult> UpdateAsync(int id, AssetDraft draft, byte[] rowVersion, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AssetWriteResult> ChangeLocationAsync(int id, AssetPlacement placement, byte[] rowVersion, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AssetWriteResult> ArchiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PagedResult<AssetHistoryEntry>?> HistoryAsync(int id, int page, int pageSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
