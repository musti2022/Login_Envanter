using System.Globalization;
using EnterpriseInventory.Application.Abstractions;
using EnterpriseInventory.Application.Assets;
using EnterpriseInventory.Application.Exports;
using EnterpriseInventory.Domain.Assets;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnterpriseInventory.Application.Reports;

/// <summary>An assignment given or taken back.</summary>
public enum AssignmentMovementKind
{
    Assigned = 0,
    Returned,
}

/// <summary>
/// Query string of <c>GET /api/reports/assignments</c>: the assignments given and returned in a period, newest first.
/// Days are days of the reporting time zone; both ends are included.
/// </summary>
public sealed record AssignmentReportRequest
{
    public const int SearchMaxLength = 100;
    public const int SearchMaxWords = 5;

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    /// <summary>The first day, <c>yyyy-MM-dd</c>, e.g. <c>2026-10-01</c>.</summary>
    public string? From { get; init; }

    /// <summary>The last day, included, in the same format.</summary>
    public string? To { get; init; }

    /// <summary><see cref="AssignmentMovementKind"/> names; both when left out.</summary>
    public string[]? Movement { get; init; }

    /// <summary>Words that must each appear in the asset code or the employee's user name or display name (case and accents ignored).</summary>
    public string? Search { get; init; }

    public string[]? AssetType { get; init; }

    /// <summary>The asset's city today (an assignment does not keep where the asset was then).</summary>
    public int? CityId { get; init; }

    /// <summary>The asset's department today.</summary>
    public int? DepartmentId { get; init; }
}

/// <summary>A validated movement report request; <see cref="Start"/> and <see cref="End"/> are the period as moments (end excluded).</summary>
public sealed record AssignmentReportCriteria(int Page, int PageSize)
{
    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public DateTimeOffset? Start { get; init; }

    public DateTimeOffset? End { get; init; }

    /// <summary>Both kinds when empty.</summary>
    public IReadOnlyList<AssignmentMovementKind> Movements { get; init; } = [];

    public IReadOnlyList<string> SearchTerms { get; init; } = [];

    public IReadOnlyList<AssetType> AssetTypes { get; init; } = [];

    public int? CityId { get; init; }

    public int? DepartmentId { get; init; }

    public bool Includes(AssignmentMovementKind kind) => Movements.Count == 0 || Movements.Contains(kind);

    public static AssignmentReportCriteria FromRequest(AssignmentReportRequest request, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(request);

        var from = AssignmentReportRequestValidator.ParseDay(request.From);
        var to = AssignmentReportRequestValidator.ParseDay(request.To);
        return new AssignmentReportCriteria(request.Page ?? 1, request.PageSize ?? AssetListRequest.DefaultPageSize)
        {
            From = from,
            To = to,
            Start = from is { } first ? ReportingTime.StartOf(first, zone) : null,
            End = to is { } last ? ReportingTime.StartOf(last.AddDays(1), zone) : null,
            Movements = (request.Movement ?? []).Select(EnumNames.Parse<AssignmentMovementKind>).Distinct().ToList(),
            SearchTerms = AssetListRequestValidator.SearchTerms(request.Search),
            AssetTypes = (request.AssetType ?? []).Select(EnumNames.Parse<AssetType>).Distinct().ToList(),
            CityId = request.CityId,
            DepartmentId = request.DepartmentId,
        };
    }
}

/// <summary>An assignment given (<see cref="At"/> = its start) or returned (its end), with the asset and the employee.</summary>
/// <param name="By">Who gave or took back the asset.</param>
/// <param name="CityName">The asset's city today.</param>
/// <param name="DepartmentName">The asset's department today.</param>
public sealed record AssignmentMovement(
    int AssignmentId,
    AssignmentMovementKind Movement,
    DateTimeOffset At,
    string By,
    int AssetId,
    string AssetCode,
    AssetType AssetType,
    string BrandName,
    string ModelName,
    string? SerialNumber,
    string EmployeeUserName,
    string EmployeeDisplayName,
    string? AssignmentDescription,
    string CityName,
    string DepartmentName,
    bool AssetArchived);

/// <summary>A page of movements; <see cref="TotalCount"/> is <see cref="AssignedCount"/> plus <see cref="ReturnedCount"/>.</summary>
public sealed record AssignmentReport(
    IReadOnlyList<AssignmentMovement> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int AssignedCount,
    int ReturnedCount);

public sealed record AssignmentReportResult(AssignmentReport? Report, IDictionary<string, string[]>? Errors);

public enum AssignmentExportOutcome
{
    Succeeded,
    Invalid,
    TooManyRows,
}

public sealed record AssignmentExportResult(AssignmentExportOutcome Outcome)
{
    public Spreadsheet? Spreadsheet { get; init; }

    public string? FileName { get; init; }

    public IDictionary<string, string[]>? Errors { get; init; }

    public int MatchCount { get; init; }

    public int MaxRows { get; init; }
}

/// <summary>"Zimmet hareketleri": who was given or gave back what, when, in a period.</summary>
public sealed partial class AssignmentReportService(
    IValidator<AssignmentReportRequest> validator,
    IReportStore reports,
    IAssetStore assets,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    IOptions<ReportingOptions> options,
    ILogger<AssignmentReportService> logger)
{
    public static readonly IReadOnlyList<SpreadsheetColumn> Columns =
    [
        new("Tarih", 18),
        new("Hareket", 16),
        new("Demirbaş Kodu", 18),
        new("Tür", 14),
        new("Marka", 16),
        new("Model", 22),
        new("Seri No", 20),
        new("Kullanıcı Adı", 18),
        new("Ad Soyad", 24),
        new("Zimmet Tanımı", 32),
        new("Şehir", 14),
        new("Departman", 22),
        new("İşlemi Yapan", 18),
        new("Arşivlenmiş", 12),
    ];

    public static string Label(AssignmentMovementKind kind) => kind switch
    {
        AssignmentMovementKind.Assigned => "Zimmet verildi",
        AssignmentMovementKind.Returned => "İade alındı",
        _ => kind.ToString(),
    };

    public async Task<AssignmentReportResult> ListAsync(AssignmentReportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return new AssignmentReportResult(null, validation.ToDictionary());
        }

        var criteria = AssignmentReportCriteria.FromRequest(request, options.Value.ResolveTimeZone());
        return new AssignmentReportResult(await reports.ListMovementsAsync(criteria, cancellationToken).ConfigureAwait(false), null);
    }

    /// <summary>Every movement that matches, as a "Hareketler" sheet and a "Bilgi" sheet; refused above <see cref="ReportingOptions.MaxExportRows"/>.</summary>
    public async Task<AssignmentExportResult> ExportAsync(AssignmentReportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return new AssignmentExportResult(AssignmentExportOutcome.Invalid) { Errors = validation.ToDictionary() };
        }

        var settings = options.Value;
        var zone = settings.ResolveTimeZone();
        var criteria = AssignmentReportCriteria.FromRequest(request, zone);
        var report = await reports.ExportMovementsAsync(criteria, settings.MaxExportRows, cancellationToken).ConfigureAwait(false);
        if (report.TotalCount > settings.MaxExportRows || report.Items.Count < report.TotalCount)
        {
            return new AssignmentExportResult(AssignmentExportOutcome.TooManyRows) { MatchCount = report.TotalCount, MaxRows = settings.MaxExportRows };
        }

        var names = await assets.FilterNamesAsync(
            new AssetListCriteria(1, 1) { CityId = criteria.CityId, DepartmentId = criteria.DepartmentId }, cancellationToken).ConfigureAwait(false);
        var localNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone);
        var userName = currentUser.UserName ?? string.Empty;
        LogExported(logger, userName, report.Items.Count);

        var rows = report.Items.Select(m => (IReadOnlyList<object?>)
        [
            ReportingTime.Local(m.At, zone),
            Label(m.Movement),
            m.AssetCode,
            AssetLabels.Of(m.AssetType),
            m.BrandName,
            m.ModelName,
            m.SerialNumber,
            m.EmployeeUserName,
            m.EmployeeDisplayName,
            m.AssignmentDescription,
            m.CityName,
            m.DepartmentName,
            m.By,
            m.AssetArchived ? "Evet" : null,
        ]).ToList();
        List<IReadOnlyList<object?>> info =
        [
            ["Rapor", "Zimmet hareketleri"],
            ["Oluşturan", userName],
            ["Oluşturulma zamanı", localNow.DateTime],
            ["Saat dilimi", ReportingTime.Describe(zone, localNow)],
            ["Başlangıç tarihi", Day(criteria.From)],
            ["Bitiş tarihi", Day(criteria.To)],
            ["Hareket", criteria.Movements.Count > 0 ? string.Join(", ", criteria.Movements.Select(Label)) : AssetSpreadsheet.All],
            ["Arama", criteria.SearchTerms.Count > 0 ? string.Join(' ', criteria.SearchTerms) : "Yok"],
            ["Tür", AssetSpreadsheet.Types(criteria.AssetTypes)],
            ["Şehir", AssetSpreadsheet.Name(criteria.CityId, names.City)],
            ["Departman", AssetSpreadsheet.Name(criteria.DepartmentId, names.Department)],
            ["Zimmet verildi", report.AssignedCount],
            ["İade alındı", report.ReturnedCount],
            ["Not", "Şehir ve departman, demirbaşın bugünkü yeridir."],
        ];

        return new AssignmentExportResult(AssignmentExportOutcome.Succeeded)
        {
            Spreadsheet = new Spreadsheet(
                "Zimmet hareketleri",
                [
                    new SpreadsheetSheet("Hareketler", Columns, rows) { Filterable = true },
                    new SpreadsheetSheet("Bilgi", [new("Bilgi", 22), new("Değer", 48)], info),
                ]),
            FileName = string.Create(CultureInfo.InvariantCulture, $"zimmet-hareketleri-{localNow:yyyy-MM-dd}"),
            MatchCount = report.TotalCount,
            MaxRows = settings.MaxExportRows,
        };

        static string Day(DateOnly? day) => day?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? AssetSpreadsheet.All;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{UserName} exported {RowCount} assignment movements")]
    private static partial void LogExported(ILogger logger, string userName, int rowCount);
}

internal sealed class AssignmentReportRequestValidator : AbstractValidator<AssignmentReportRequest>
{
    /// <summary>Days outside this range are refused: nothing in the inventory is that old, and the end day plus one must exist.</summary>
    public static readonly DateOnly FirstDay = new(2000, 1, 1);

    public static readonly DateOnly LastDay = new(2099, 12, 31);

    public AssignmentReportRequestValidator()
    {
        RuleFor(r => r.Page).MustBePage();
        RuleFor(r => r.PageSize).MustBePageSize();

        RuleFor(r => r.From)
            .Must(text => ParseDay(text) is not null)
            .When(r => !string.IsNullOrWhiteSpace(r.From))
            .WithMessage("Başlangıç tarihi geçersiz. 01.01.2000 ile 31.12.2099 arasında, YYYY-AA-GG biçiminde olmalıdır (ör. 2026-10-01).");

        RuleFor(r => r.To)
            .Cascade(CascadeMode.Stop)
            .Must(text => ParseDay(text) is not null)
            .WithMessage("Bitiş tarihi geçersiz. 01.01.2000 ile 31.12.2099 arasında, YYYY-AA-GG biçiminde olmalıdır (ör. 2026-10-31).")
            .Must((request, text) => ParseDay(request.From) is not { } from || from <= ParseDay(text))
            .WithMessage("Bitiş tarihi başlangıç tarihinden önce olamaz.")
            .When(r => !string.IsNullOrWhiteSpace(r.To));

        RuleFor(r => r.Movement)
            .Must(names => names is null || names.All(EnumNames.IsDefined<AssignmentMovementKind>))
            .WithMessage($"Hareket filtresi geçersiz. Geçerli değerler: {string.Join(", ", Enum.GetNames<AssignmentMovementKind>())}.");

        RuleFor(r => r.Search)
            .Cascade(CascadeMode.Stop)
            .Must(text => (text?.Trim().Length ?? 0) <= AssignmentReportRequest.SearchMaxLength)
            .WithMessage($"Arama metni en fazla {AssignmentReportRequest.SearchMaxLength} karakter olabilir.")
            .Must(text => text is null || !text.Any(char.IsControl)).WithMessage("Arama metni geçersiz karakter içeriyor.")
            .Must(text => AssetListRequestValidator.SearchTerms(text).Count <= AssignmentReportRequest.SearchMaxWords)
            .WithMessage($"Arama en fazla {AssignmentReportRequest.SearchMaxWords} kelime içerebilir.");

        RuleFor(r => r.AssetType)
            .Must(names => names is null || names.All(EnumNames.IsDefined<AssetType>))
            .WithMessage($"Demirbaş türü filtresi geçersiz. Geçerli değerler: {string.Join(", ", Enum.GetNames<AssetType>())}.");

        RuleFor(r => r.CityId).GreaterThan(0).WithMessage("Şehir filtresi geçersiz.");
        RuleFor(r => r.DepartmentId).GreaterThan(0).WithMessage("Departman filtresi geçersiz.");
    }

    /// <summary>A day written <c>yyyy-MM-dd</c> between <see cref="FirstDay"/> and <see cref="LastDay"/>, or <c>null</c>.</summary>
    public static DateOnly? ParseDay(string? text) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
        && day >= FirstDay
        && day <= LastDay
            ? day
            : null;
}
