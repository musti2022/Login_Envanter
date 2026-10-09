using FluentValidation;

namespace EnterpriseInventory.Application.Assets;

/// <summary>Query string of <c>GET /api/assets</c>, as typed by the caller; checked by <see cref="AssetListRequestValidator"/>.</summary>
public sealed record AssetListRequest
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    /// <summary>Keeps the row offset (page × page size) far from overflowing.</summary>
    public const int MaxPage = 100_000;

    public int? Page { get; init; }

    public int? PageSize { get; init; }
}

/// <summary>A validated list request.</summary>
public sealed record AssetListCriteria(int Page, int PageSize);

internal sealed class AssetListRequestValidator : AbstractValidator<AssetListRequest>
{
    public AssetListRequestValidator()
    {
        RuleFor(r => r.Page).MustBePage();
        RuleFor(r => r.PageSize).MustBePageSize();
    }
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
