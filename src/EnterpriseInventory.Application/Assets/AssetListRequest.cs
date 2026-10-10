using EnterpriseInventory.Domain.Assets;
using FluentValidation;

namespace EnterpriseInventory.Application.Assets;

/// <summary>
/// Query string of <c>GET /api/assets</c>, as typed by the caller; checked by <see cref="AssetListRequestValidator"/>.
/// Every filter given must match (AND); a filter that takes several values (<c>status=Faulty&amp;status=Retired</c>)
/// matches any of them.
/// </summary>
public sealed record AssetListRequest
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    /// <summary>Keeps the row offset (page × page size) far from overflowing.</summary>
    public const int MaxPage = 100_000;

    public const int SearchMaxLength = 100;
    public const int SearchMaxWords = 5;

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    /// <summary>
    /// Words that must each appear (as typed, ignoring case the Turkish way) in one of the table's text columns:
    /// asset code, computer name, serial number, brand, model, city, department, location, and the holder's user
    /// name, display name or assignment description. <c>%</c> and <c>_</c> are plain characters.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>Status names, e.g. <c>Available</c>, <c>Assigned</c>.</summary>
    public string[]? Status { get; init; }

    /// <summary>Asset type names, e.g. <c>Laptop</c>.</summary>
    public string[]? AssetType { get; init; }

    public int? BrandId { get; init; }

    public int? ModelId { get; init; }

    public int? CityId { get; init; }

    public int? DepartmentId { get; init; }

    public int? LocationId { get; init; }

    /// <summary><c>true</c> lists only archived assets; otherwise only those that are not archived.</summary>
    public bool? Archived { get; init; }

    /// <summary>An <see cref="AssetSortField"/> name, e.g. <c>computerName</c>; <c>assetCode</c> when left out.</summary>
    public string? SortBy { get; init; }

    /// <summary><c>asc</c> (the default) or <c>desc</c>.</summary>
    public string? SortDirection { get; init; }
}

/// <summary>
/// The columns the inventory can be sorted by. Statuses and types sort in the order of their values, not by
/// their Turkish labels; empty values come first in ascending order and last in descending order.
/// </summary>
public enum AssetSortField
{
    AssetCode = 0,
    ComputerName,
    SerialNumber,
    BrandName,
    ModelName,
    AssetType,
    Status,
    CityName,
    DepartmentName,
    LocationName,
    AssignedUserName,
    AssignedDisplayName,
    CreatedAt,
    UpdatedAt,
}

/// <summary>A validated list request; empty filter lists match everything.</summary>
public sealed record AssetListCriteria(int Page, int PageSize)
{
    public IReadOnlyList<string> SearchTerms { get; init; } = [];

    public IReadOnlyList<AssetStatus> Statuses { get; init; } = [];

    public IReadOnlyList<AssetType> AssetTypes { get; init; } = [];

    public int? BrandId { get; init; }

    public int? ModelId { get; init; }

    public int? CityId { get; init; }

    public int? DepartmentId { get; init; }

    public int? LocationId { get; init; }

    public bool Archived { get; init; }

    public AssetSortField SortBy { get; init; } = AssetSortField.AssetCode;

    public bool Descending { get; init; }

    public static AssetListCriteria From(AssetListRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new AssetListCriteria(request.Page ?? 1, request.PageSize ?? AssetListRequest.DefaultPageSize)
        {
            SearchTerms = AssetListRequestValidator.SearchTerms(request.Search),
            Statuses = (request.Status ?? []).Select(EnumNames.Parse<AssetStatus>).Distinct().ToList(),
            AssetTypes = (request.AssetType ?? []).Select(EnumNames.Parse<AssetType>).Distinct().ToList(),
            BrandId = request.BrandId,
            ModelId = request.ModelId,
            CityId = request.CityId,
            DepartmentId = request.DepartmentId,
            LocationId = request.LocationId,
            Archived = request.Archived ?? false,
            SortBy = request.SortBy is null ? AssetSortField.AssetCode : EnumNames.Parse<AssetSortField>(request.SortBy),
            Descending = string.Equals(request.SortDirection, "desc", StringComparison.OrdinalIgnoreCase),
        };
    }
}

internal sealed class AssetListRequestValidator : AbstractValidator<AssetListRequest>
{
    public AssetListRequestValidator()
    {
        RuleFor(r => r.Page).MustBePage();
        RuleFor(r => r.PageSize).MustBePageSize();

        RuleFor(r => r.Search)
            .Cascade(CascadeMode.Stop)
            .Must(text => (text?.Trim().Length ?? 0) <= AssetListRequest.SearchMaxLength)
            .WithMessage($"Arama metni en fazla {AssetListRequest.SearchMaxLength} karakter olabilir.")
            .Must(text => text is null || !text.Any(char.IsControl)).WithMessage("Arama metni geçersiz karakter içeriyor.")
            .Must(text => SearchTerms(text).Count <= AssetListRequest.SearchMaxWords)
            .WithMessage($"Arama en fazla {AssetListRequest.SearchMaxWords} kelime içerebilir.");

        RuleFor(r => r.Status)
            .Must(names => names is null || names.All(EnumNames.IsDefined<AssetStatus>))
            .WithMessage($"Durum filtresi geçersiz. Geçerli değerler: {Names<AssetStatus>()}.");

        RuleFor(r => r.AssetType)
            .Must(names => names is null || names.All(EnumNames.IsDefined<AssetType>))
            .WithMessage($"Demirbaş türü filtresi geçersiz. Geçerli değerler: {Names<AssetType>()}.");

        RuleFor(r => r.BrandId).GreaterThan(0).WithMessage("Marka filtresi geçersiz.");
        RuleFor(r => r.ModelId).GreaterThan(0).WithMessage("Model filtresi geçersiz.");
        RuleFor(r => r.CityId).GreaterThan(0).WithMessage("Şehir filtresi geçersiz.");
        RuleFor(r => r.DepartmentId).GreaterThan(0).WithMessage("Departman filtresi geçersiz.");
        RuleFor(r => r.LocationId).GreaterThan(0).WithMessage("Lokasyon filtresi geçersiz.");

        RuleFor(r => r.SortBy)
            .Must(EnumNames.IsDefined<AssetSortField>)
            .When(r => r.SortBy is not null)
            .WithMessage($"Sıralama alanı geçersiz. Geçerli alanlar: {CamelCaseNames<AssetSortField>()}.");

        RuleFor(r => r.SortDirection)
            .Must(direction => direction is null
                || string.Equals(direction, "asc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Sıralama yönü 'asc' veya 'desc' olmalıdır.");
    }

    /// <summary>The words of a search, split on any whitespace.</summary>
    public static IReadOnlyList<string> SearchTerms(string? search) =>
        search is null ? [] : search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToList();

    private static string Names<T>()
        where T : struct, Enum =>
        string.Join(", ", Enum.GetNames<T>());

    private static string CamelCaseNames<T>()
        where T : struct, Enum =>
        string.Join(", ", Enum.GetNames<T>().Select(name => char.ToLowerInvariant(name[0]) + name[1..]));
}

/// <summary>Paging rules every asset list shares (the inventory and an asset's history).</summary>
internal static class PagingRules
{
    public static IRuleBuilderOptions<T, int?> MustBePage<T>(this IRuleBuilderInitial<T, int?> rule) =>
        rule.InclusiveBetween(1, AssetListRequest.MaxPage)
            .WithMessage($"Sayfa numarası 1 ile {AssetListRequest.MaxPage} arasında olmalıdır.");

    public static IRuleBuilderOptions<T, int?> MustBePageSize<T>(this IRuleBuilderInitial<T, int?> rule) =>
        rule.InclusiveBetween(1, AssetListRequest.MaxPageSize)
            .WithMessage($"Sayfa boyutu 1 ile {AssetListRequest.MaxPageSize} arasında olmalıdır.");
}
