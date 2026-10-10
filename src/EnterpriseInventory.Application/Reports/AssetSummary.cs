using System.Globalization;
using EnterpriseInventory.Application.Abstractions;
using EnterpriseInventory.Application.Assets;
using EnterpriseInventory.Application.Exports;
using EnterpriseInventory.Domain.Assets;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnterpriseInventory.Application.Reports;

/// <summary>What the inventory summary counts assets by.</summary>
public enum AssetGrouping
{
    City = 0,
    Department,
    Location,
    Brand,
    Model,
    AssetType,
    Status,
}

/// <summary>
/// Query string of <c>GET /api/reports/asset-summary</c>: a grouping and the inventory list's filters, with the
/// same names, rules and Turkish messages as <c>GET /api/assets</c>.
/// </summary>
public sealed record AssetSummaryRequest
{
    /// <summary>An <see cref="AssetGrouping"/> name, ignoring case; <c>city</c> when left out.</summary>
    public string? GroupBy { get; init; }

    public string? Search { get; init; }

    public string[]? Status { get; init; }

    public string[]? AssetType { get; init; }

    public int? BrandId { get; init; }

    public int? ModelId { get; init; }

    public int? CityId { get; init; }

    public int? DepartmentId { get; init; }

    public int? LocationId { get; init; }

    /// <summary>The filters as the inventory list takes them, so both are checked and applied by the same code.</summary>
    internal AssetListRequest Filters() => new()
    {
        Search = Search,
        Status = Status,
        AssetType = AssetType,
        BrandId = BrandId,
        ModelId = ModelId,
        CityId = CityId,
        DepartmentId = DepartmentId,
        LocationId = LocationId,
    };
}

/// <summary>
/// One group's assets by status. <see cref="InventoryFilter"/> is the inventory list's query that shows this
/// group's assets (to be added to the report's own filters); <c>null</c> when the list cannot (assets with no
/// location).
/// </summary>
/// <param name="Key">Unique within the report: an ID, a status or type name, or <c>none</c>.</param>
public sealed record AssetSummaryRow(
    string Key,
    string Name,
    IReadOnlyDictionary<string, string>? InventoryFilter,
    int TotalCount,
    int AssignedCount,
    int AvailableCount,
    int FaultyCount,
    int RetiredCount);

/// <summary>Every group that has assets, largest first (Turkish alphabetical order breaking ties), and their total.</summary>
public sealed record AssetSummary(AssetGrouping GroupBy, IReadOnlyList<AssetSummaryRow> Rows, AssetSummaryRow Total);

/// <summary>
/// A group as the database counts it: a lookup's ID and name (with the city of a location, the brand of a model), or a
/// type or status number in <see cref="Id"/>; all <c>null</c> for assets with no location.
/// </summary>
public sealed record AssetGroupCount(
    int? Id,
    string? Name,
    int? ParentId,
    string? ParentName,
    int TotalCount,
    int AssignedCount,
    int AvailableCount,
    int FaultyCount,
    int RetiredCount);

public sealed record AssetSummaryResult(AssetSummary? Summary, IDictionary<string, string[]>? Errors);

public sealed record AssetSummaryExportResult(Spreadsheet? Spreadsheet, string? FileName, IDictionary<string, string[]>? Errors);

/// <summary>Reads the figures of the report screen.</summary>
public interface IReportStore
{
    /// <summary>The assets that are not archived and match <paramref name="criteria"/>, counted per group, unsorted.</summary>
    Task<IReadOnlyList<AssetGroupCount>> CountAssetsAsync(AssetListCriteria criteria, AssetGrouping grouping, CancellationToken cancellationToken);

    /// <summary>A page of the assignments and returns that match, newest first, with how many of each match.</summary>
    Task<AssignmentReport> ListMovementsAsync(AssignmentReportCriteria criteria, CancellationToken cancellationToken);

    /// <summary>Every movement that matches, or none (only the count) when more than <paramref name="maxRows"/> match.</summary>
    Task<AssignmentReport> ExportMovementsAsync(AssignmentReportCriteria criteria, int maxRows, CancellationToken cancellationToken);
}

/// <summary>"Envanter özeti": assets that are not archived, counted by status per city, department, brand and so on.</summary>
public sealed partial class AssetSummaryService(
    IValidator<AssetListRequest> filterValidator,
    IReportStore reports,
    IAssetStore assets,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    IOptions<ReportingOptions> options,
    ILogger<AssetSummaryService> logger)
{
    private static readonly StringComparer TurkishOrder = StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: false);

    public async Task<AssetSummaryResult> GetAsync(AssetSummaryRequest request, CancellationToken cancellationToken)
    {
        var (criteria, grouping, errors) = await CheckAsync(request, cancellationToken).ConfigureAwait(false);
        return errors is null
            ? new AssetSummaryResult(await SummarizeAsync(criteria!, grouping, cancellationToken).ConfigureAwait(false), null)
            : new AssetSummaryResult(null, errors);
    }

    /// <summary>The same report as an Excel workbook: an "Özet" sheet with a total row, and a "Bilgi" sheet.</summary>
    public async Task<AssetSummaryExportResult> ExportAsync(AssetSummaryRequest request, CancellationToken cancellationToken)
    {
        var (criteria, grouping, errors) = await CheckAsync(request, cancellationToken).ConfigureAwait(false);
        if (errors is not null)
        {
            return new AssetSummaryExportResult(null, null, errors);
        }

        var summary = await SummarizeAsync(criteria!, grouping, cancellationToken).ConfigureAwait(false);
        var names = await assets.FilterNamesAsync(criteria!, cancellationToken).ConfigureAwait(false);
        var zone = options.Value.ResolveTimeZone();
        var localNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone);
        var userName = currentUser.UserName ?? string.Empty;
        LogExported(logger, userName, grouping, summary.Rows.Count);

        var label = GroupLabel(grouping);
        List<IReadOnlyList<object?>> rows = [.. summary.Rows.Select(Cells), Cells(summary.Total)];
        List<IReadOnlyList<object?>> info =
        [
            ["Rapor", "Envanter özeti"],
            ["Gruplama", label],
            ["Kapsam", "Arşivlenmemiş demirbaşlar"],
            ["Oluşturan", userName],
            ["Oluşturulma zamanı", localNow.DateTime],
            ["Saat dilimi", ReportingTime.Describe(zone, localNow)],
            ["Demirbaş sayısı", summary.Total.TotalCount],
            [$"{label} sayısı", summary.Rows.Count],
            .. AssetSpreadsheet.FilterRows(criteria!, names),
        ];
        var spreadsheet = new Spreadsheet(
            "Envanter özeti",
            [
                new SpreadsheetSheet(
                    "Özet",
                    [new(label, 32), new("Toplam", 10), new("Zimmetli", 10), new("Boşta", 10), new("Arızalı", 10), new("Hurda", 10)],
                    rows),
                new SpreadsheetSheet("Bilgi", [new("Bilgi", 22), new("Değer", 48)], info),
            ]);
        var fileName = string.Create(CultureInfo.InvariantCulture, $"envanter-ozeti-{FileSlug(grouping)}-{localNow:yyyy-MM-dd}");
        return new AssetSummaryExportResult(spreadsheet, fileName, null);

        static IReadOnlyList<object?> Cells(AssetSummaryRow row) =>
            [row.Name, row.TotalCount, row.AssignedCount, row.AvailableCount, row.FaultyCount, row.RetiredCount];
    }

    /// <summary>The Turkish name of a grouping, as the screen and the file head their first column.</summary>
    public static string GroupLabel(AssetGrouping grouping) => grouping switch
    {
        AssetGrouping.City => "Şehir",
        AssetGrouping.Department => "Departman",
        AssetGrouping.Location => "Lokasyon",
        AssetGrouping.Brand => "Marka",
        AssetGrouping.Model => "Model",
        AssetGrouping.AssetType => "Tür",
        AssetGrouping.Status => "Durum",
        _ => grouping.ToString(),
    };

    private static string FileSlug(AssetGrouping grouping) => grouping switch
    {
        AssetGrouping.City => "sehir",
        AssetGrouping.Department => "departman",
        AssetGrouping.Location => "lokasyon",
        AssetGrouping.Brand => "marka",
        AssetGrouping.Model => "model",
        AssetGrouping.AssetType => "tur",
        AssetGrouping.Status => "durum",
        _ => "grup",
    };

    private async Task<(AssetListCriteria? Criteria, AssetGrouping Grouping, IDictionary<string, string[]>? Errors)> CheckAsync(
        AssetSummaryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var filters = request.Filters();
        var validation = await filterValidator.ValidateAsync(filters, cancellationToken).ConfigureAwait(false);
        var errors = validation.ToDictionary();
        var grouping = AssetGrouping.City;
        if (request.GroupBy is not null && !EnumNames.TryParse(request.GroupBy, out grouping))
        {
            var names = string.Join(", ", Enum.GetNames<AssetGrouping>().Select(n => char.ToLowerInvariant(n[0]) + n[1..]));
            errors[nameof(AssetSummaryRequest.GroupBy)] = [$"Gruplama geçersiz. Geçerli değerler: {names}."];
        }

        return errors.Count > 0 ? (null, grouping, errors) : (AssetListCriteria.From(filters), grouping, null);
    }

    private async Task<AssetSummary> SummarizeAsync(AssetListCriteria criteria, AssetGrouping grouping, CancellationToken cancellationToken)
    {
        var groups = await reports.CountAssetsAsync(criteria, grouping, cancellationToken).ConfigureAwait(false);
        var rows = groups.Select(g => Row(g, grouping))
            .OrderByDescending(r => r.TotalCount)
            .ThenBy(r => r.Name, TurkishOrder)
            .ThenBy(r => r.Key, StringComparer.Ordinal)
            .ToList();
        var total = new AssetSummaryRow(
            "total",
            "Toplam",
            null,
            rows.Sum(r => r.TotalCount),
            rows.Sum(r => r.AssignedCount),
            rows.Sum(r => r.AvailableCount),
            rows.Sum(r => r.FaultyCount),
            rows.Sum(r => r.RetiredCount));
        return new AssetSummary(grouping, rows, total);
    }

    private static AssetSummaryRow Row(AssetGroupCount group, AssetGrouping grouping)
    {
        string Id(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

        var (key, name, filter) = grouping switch
        {
            AssetGrouping.AssetType when group.Id is { } type =>
                (((AssetType)type).ToString(), AssetLabels.Of((AssetType)type), Filter(("assetType", ((AssetType)type).ToString()))),
            AssetGrouping.Status when group.Id is { } status =>
                (((AssetStatus)status).ToString(), AssetLabels.Of((AssetStatus)status), Filter(("status", ((AssetStatus)status).ToString()))),
            AssetGrouping.Location when group.Id is null => ("none", "Lokasyon belirtilmemiş", null),

            // The list filters a location within its city, and a model within its brand.
            AssetGrouping.Location => (Id(group.Id), $"{group.Name} ({group.ParentName})", Filter(("cityId", Id(group.ParentId)), ("locationId", Id(group.Id)))),
            AssetGrouping.Model => (Id(group.Id), $"{group.ParentName} {group.Name}", Filter(("brandId", Id(group.ParentId)), ("modelId", Id(group.Id)))),
            AssetGrouping.Brand => (Id(group.Id), group.Name ?? string.Empty, Filter(("brandId", Id(group.Id)))),
            AssetGrouping.Department => (Id(group.Id), group.Name ?? string.Empty, Filter(("departmentId", Id(group.Id)))),
            _ => (Id(group.Id), group.Name ?? string.Empty, Filter(("cityId", Id(group.Id)))),
        };
        return new AssetSummaryRow(
            key, name, filter, group.TotalCount, group.AssignedCount, group.AvailableCount, group.FaultyCount, group.RetiredCount);

        static Dictionary<string, string> Filter(params (string Name, string Value)[] values) =>
            values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{UserName} exported the asset summary by {Grouping} ({RowCount} rows)")]
    private static partial void LogExported(ILogger logger, string userName, AssetGrouping grouping, int rowCount);
}
