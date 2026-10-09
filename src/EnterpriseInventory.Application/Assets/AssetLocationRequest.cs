using FluentValidation;

namespace EnterpriseInventory.Application.Assets;

/// <summary>
/// Body of <c>PUT /api/assets/{id}/location</c>: where the asset is now (city, optional location in that city) and
/// which department it belongs to, plus the <see cref="RowVersion"/> the caller read. Nothing else of the asset
/// changes, so a move needs no full edit and is recorded as a location change only.
/// </summary>
public sealed record ChangeAssetLocationRequest
{
    public int? CityId { get; init; }

    public int? DepartmentId { get; init; }

    /// <summary>Optional; when given it must be a location of <see cref="CityId"/>.</summary>
    public int? LocationId { get; init; }

    /// <summary>The asset's <c>rowVersion</c> from the caller's last read; a stale one is refused with 409.</summary>
    public string? RowVersion { get; init; }
}

/// <summary>A validated move: what <see cref="IAssetStore.ChangeLocationAsync"/> writes.</summary>
public sealed record AssetPlacement(int CityId, int DepartmentId, int? LocationId);

/// <summary>Field rules of a move; the store checks that the values exist, are active and fit together.</summary>
internal sealed class ChangeAssetLocationRequestValidator : AbstractValidator<ChangeAssetLocationRequest>
{
    public ChangeAssetLocationRequestValidator()
    {
        RuleFor(r => r.CityId).NotNull().WithMessage("Şehir seçilmelidir.").GreaterThan(0).WithMessage("Şehir geçersiz.");
        RuleFor(r => r.DepartmentId).NotNull().WithMessage("Departman seçilmelidir.").GreaterThan(0).WithMessage("Departman geçersiz.");
        RuleFor(r => r.LocationId).GreaterThan(0).WithMessage("Lokasyon geçersiz.");
        RuleFor(r => r.RowVersion).MustBeRowVersion();
    }
}
