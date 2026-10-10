using System.Globalization;
using EnterpriseInventory.Application.Abstractions;
using EnterpriseInventory.Application.Exports;
using EnterpriseInventory.Application.Reports;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnterpriseInventory.Application.Assets;

/// <summary>
/// The assets an export writes: all that match, in list order, or (<see cref="Items"/> empty) only how many matched
/// when that is more than the export may hold.
/// </summary>
public sealed record AssetExportRows(int MatchCount, IReadOnlyList<AssetListItem> Items);

/// <summary>Names of the lookups a list is filtered on; <c>null</c> for a filter that is not set or an ID nothing has.</summary>
public sealed record AssetFilterNames(string? Brand, string? Model, string? City, string? Department, string? Location);

public enum AssetExportOutcome
{
    Succeeded,
    Invalid,

    /// <summary>More assets match than <see cref="ReportingOptions.MaxExportRows"/>; nothing was written.</summary>
    TooManyRows,
}

public sealed record AssetExportResult(AssetExportOutcome Outcome)
{
    public Spreadsheet? Spreadsheet { get; init; }

    /// <summary>Without the extension, e.g. <c>envanter-2026-10-10</c> (the day in the reporting time zone).</summary>
    public string? FileName { get; init; }

    public IDictionary<string, string[]>? Errors { get; init; }

    public int MatchCount { get; init; }

    public int MaxRows { get; init; }
}

/// <summary>
/// "Excel'e aktar": the inventory list with the same filters and order as <c>GET /api/assets</c>, every page, as a
/// spreadsheet with a second sheet saying what was exported, by whom and when.
/// </summary>
public sealed partial class AssetExportService(
    IValidator<AssetListRequest> validator,
    IAssetStore store,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    IOptions<ReportingOptions> options,
    ILogger<AssetExportService> logger)
{
    public async Task<AssetExportResult> ExportAsync(AssetListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return new AssetExportResult(AssetExportOutcome.Invalid) { Errors = validation.ToDictionary() };
        }

        var settings = options.Value;
        var criteria = AssetListCriteria.From(request);
        var rows = await store.ExportAsync(criteria, settings.MaxExportRows, cancellationToken).ConfigureAwait(false);
        if (rows.MatchCount > settings.MaxExportRows)
        {
            return new AssetExportResult(AssetExportOutcome.TooManyRows) { MatchCount = rows.MatchCount, MaxRows = settings.MaxExportRows };
        }

        var names = await store.FilterNamesAsync(criteria, cancellationToken).ConfigureAwait(false);
        var userName = currentUser.UserName ?? string.Empty;
        var spreadsheet = AssetSpreadsheet.Build(rows.Items, criteria, names, userName, timeProvider.GetUtcNow(), settings.ResolveTimeZone(), out var day);
        LogExported(logger, rows.Items.Count, userName);

        return new AssetExportResult(AssetExportOutcome.Succeeded)
        {
            Spreadsheet = spreadsheet,
            FileName = string.Create(CultureInfo.InvariantCulture, $"{(criteria.Archived ? "envanter-arsiv" : "envanter")}-{day:yyyy-MM-dd}"),
            MatchCount = rows.Items.Count,
            MaxRows = settings.MaxExportRows,
        };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{UserName} exported {RowCount} assets")]
    private static partial void LogExported(ILogger logger, int rowCount, string userName);
}

/// <summary>The inventory workbook: an "Envanter" sheet with the list and a "Bilgi" sheet describing it.</summary>
internal static class AssetSpreadsheet
{
    public const string ListSheet = "Envanter";
    public const string InfoSheet = "Bilgi";

    internal const string All = "Tümü";

    public static readonly IReadOnlyList<SpreadsheetColumn> Columns =
    [
        new("Demirbaş Kodu", 18),
        new("Kullanıcı Adı", 18),
        new("Ad Soyad", 24),
        new("Bilgisayar Adı", 18),
        new("Marka", 16),
        new("Model", 22),
        new("Seri No", 20),
        new("Zimmet Tanımı", 32),
        new("Şehir", 14),
        new("Lokasyon", 20),
        new("Departman", 22),
        new("Tür", 14),
        new("Durum", 12),
        new("Eklenme Zamanı", 18),
        new("Son Değişiklik", 18),
    ];

    /// <param name="now">When the file is made; its day in <paramref name="zone"/> is returned in <paramref name="day"/>.</param>
    public static Spreadsheet Build(
        IReadOnlyList<AssetListItem> items,
        AssetListCriteria criteria,
        AssetFilterNames names,
        string userName,
        DateTimeOffset now,
        TimeZoneInfo zone,
        out DateOnly day)
    {
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        day = DateOnly.FromDateTime(localNow.DateTime);

        var rows = new List<IReadOnlyList<object?>>(items.Count);
        foreach (var a in items)
        {
            rows.Add(
            [
                a.AssetCode,
                a.AssignedUserName,
                a.AssignedDisplayName,
                a.ComputerName,
                a.BrandName,
                a.ModelName,
                a.SerialNumber,
                a.AssignmentDescription,
                a.CityName,
                a.LocationName,
                a.DepartmentName,
                AssetLabels.Of(a.AssetType),
                AssetLabels.Of(a.Status),
                Local(a.CreatedAt, zone),
                a.UpdatedAt is { } updatedAt ? Local(updatedAt, zone) : null,
            ]);
        }

        List<IReadOnlyList<object?>> info =
        [
            ["Rapor", criteria.Archived ? "Arşivlenmiş demirbaşlar" : "Envanter listesi"],
            ["Oluşturan", userName],
            ["Oluşturulma zamanı", localNow.DateTime],
            ["Saat dilimi", ReportingTime.Describe(zone, localNow)],
            ["Demirbaş sayısı", items.Count],
            .. FilterRows(criteria, names),
            ["Sıralama", $"{AssetLabels.Of(criteria.SortBy)}, {(criteria.Descending ? "azalan" : "artan")}"],
        ];

        return new Spreadsheet(
            criteria.Archived ? "Arşivlenmiş demirbaşlar" : "Envanter listesi",
            [
                new SpreadsheetSheet(ListSheet, Columns, rows) { Filterable = true },
                new SpreadsheetSheet(InfoSheet, [new("Bilgi", 22), new("Değer", 48)], info),
            ]);
    }

    /// <summary>The "Bilgi" rows naming the filters a list was made with; "Tümü" for a filter that is not set.</summary>
    internal static IEnumerable<IReadOnlyList<object?>> FilterRows(AssetListCriteria criteria, AssetFilterNames names) =>
    [
        ["Arama", criteria.SearchTerms.Count > 0 ? string.Join(' ', criteria.SearchTerms) : "Yok"],
        ["Durum", criteria.Statuses.Count > 0 ? string.Join(", ", criteria.Statuses.Select(AssetLabels.Of)) : All],
        ["Tür", Types(criteria.AssetTypes)],
        ["Marka", Name(criteria.BrandId, names.Brand)],
        ["Model", Name(criteria.ModelId, names.Model)],
        ["Şehir", Name(criteria.CityId, names.City)],
        ["Departman", Name(criteria.DepartmentId, names.Department)],
        ["Lokasyon", Name(criteria.LocationId, names.Location)],
    ];

    internal static string Types(IReadOnlyList<Domain.Assets.AssetType> types) =>
        types.Count > 0 ? string.Join(", ", types.Select(AssetLabels.Of)) : All;

    /// <summary>A filter's name; "Tümü" when it is not set, and the ID when nothing has it (any more).</summary>
    internal static string Name(int? id, string? name) => id switch
    {
        null => All,
        { } value when name is null => string.Create(CultureInfo.InvariantCulture, $"Kimlik {value} (kayıt bulunamadı)"),
        _ => name!,
    };

    private static DateTime Local(DateTimeOffset value, TimeZoneInfo zone) => ReportingTime.Local(value, zone);
}
