using EnterpriseInventory.Domain.Assets;
using FluentValidation;

namespace EnterpriseInventory.Application.Assets;

/// <summary>
/// Body of <c>POST /api/assets</c>, as typed by the caller. Enums are sent by name ("Laptop", "Faulty"). The brand
/// is not sent: it is always the model's brand.
/// </summary>
public record SaveAssetRequest
{
    public string? AssetCode { get; init; }

    public string? AssetType { get; init; }

    /// <summary>
    /// Optional when creating (<c>Available</c>), required when updating. "Assigned" is only reached by assigning
    /// the asset; an update may send it back unchanged for an asset that is already assigned.
    /// </summary>
    public string? Status { get; init; }

    public int? ModelId { get; init; }

    public int? CityId { get; init; }

    public int? DepartmentId { get; init; }

    public int? LocationId { get; init; }

    public string? ComputerName { get; init; }

    /// <summary>Stored without whitespace and in upper case (<see cref="Asset.NormalizeSerialNumber"/>).</summary>
    public string? SerialNumber { get; init; }

    public string? Description { get; init; }
}

/// <summary>A validated asset, with every value parsed; what <see cref="IAssetStore"/> writes.</summary>
/// <param name="Status"><c>null</c> keeps the current status (a new asset starts <c>Available</c>).</param>
public sealed record AssetDraft(
    string AssetCode,
    AssetType AssetType,
    AssetStatus? Status,
    int ModelId,
    int CityId,
    int DepartmentId,
    int? LocationId,
    string? ComputerName,
    string? SerialNumber,
    string? Description)
{
    public static AssetDraft From(SaveAssetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new AssetDraft(
            request.AssetCode!.Trim(),
            EnumNames.Parse<AssetType>(request.AssetType),
            request.Status is null ? null : EnumNames.Parse<AssetStatus>(request.Status),
            request.ModelId!.Value,
            request.CityId!.Value,
            request.DepartmentId!.Value,
            request.LocationId,
            Blank(request.ComputerName),
            Asset.NormalizeSerialNumber(request.SerialNumber),
            Blank(request.Description));
    }

    /// <summary>Empty text is stored as <c>null</c>, so many assets can have no serial number.</summary>
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Body of <c>PUT /api/assets/{id}</c>: every field of the asset, as it should be after the update, plus the
/// <see cref="RowVersion"/> the caller read. Fields left out are cleared, except <see cref="SaveAssetRequest.Status"/>,
/// which is required.
/// </summary>
public sealed record UpdateAssetRequest : SaveAssetRequest
{
    /// <summary>The asset's <c>rowVersion</c> from the caller's last read; a stale one is refused with 409.</summary>
    public string? RowVersion { get; init; }
}

/// <summary>Rules every asset write shares, whether it creates the asset or updates it.</summary>
internal abstract class AssetFieldsValidator<T> : AbstractValidator<T>
    where T : SaveAssetRequest
{
    protected AssetFieldsValidator()
    {
        RuleFor(r => r.AssetCode)
            .Cascade(CascadeMode.Stop)
            .Must(code => !string.IsNullOrWhiteSpace(code)).WithMessage("Demirbaş kodu zorunludur.")
            .Must(code => code!.Trim().Length <= Asset.AssetCodeMaxLength)
            .WithMessage($"Demirbaş kodu en fazla {Asset.AssetCodeMaxLength} karakter olabilir.")
            .Must(NoControlCharacters).WithMessage("Demirbaş kodu geçersiz karakter içeriyor.");

        RuleFor(r => r.AssetType)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Demirbaş türü zorunludur.")
            .Must(EnumNames.IsDefined<AssetType>).WithMessage("Demirbaş türü geçersiz.");

        RuleFor(r => r.ModelId).NotNull().WithMessage("Model seçilmelidir.").GreaterThan(0).WithMessage("Model geçersiz.");
        RuleFor(r => r.CityId).NotNull().WithMessage("Şehir seçilmelidir.").GreaterThan(0).WithMessage("Şehir geçersiz.");
        RuleFor(r => r.DepartmentId).NotNull().WithMessage("Departman seçilmelidir.").GreaterThan(0).WithMessage("Departman geçersiz.");
        RuleFor(r => r.LocationId).GreaterThan(0).WithMessage("Lokasyon geçersiz.");

        RuleFor(r => r.ComputerName)
            .Cascade(CascadeMode.Stop)
            .Must(name => Trimmed(name) <= Asset.ComputerNameMaxLength)
            .WithMessage($"Bilgisayar adı en fazla {Asset.ComputerNameMaxLength} karakter olabilir.")
            .Must(NoControlCharacters).WithMessage("Bilgisayar adı geçersiz karakter içeriyor.");

        RuleFor(r => r.SerialNumber)
            .Cascade(CascadeMode.Stop)
            .Must(serial => (Asset.NormalizeSerialNumber(serial)?.Length ?? 0) <= Asset.SerialNumberMaxLength)
            .WithMessage($"Seri numarası en fazla {Asset.SerialNumberMaxLength} karakter olabilir.")
            .Must(NoControlCharacters).WithMessage("Seri numarası geçersiz karakter içeriyor.");

        RuleFor(r => r.Description)
            .Must(text => Trimmed(text) <= Asset.DescriptionMaxLength)
            .WithMessage($"Açıklama en fazla {Asset.DescriptionMaxLength} karakter olabilir.");
    }

    private static int Trimmed(string? value) => value?.Trim().Length ?? 0;

    private static bool NoControlCharacters(string? value) => value is null || !value.Any(char.IsControl);
}

internal sealed class SaveAssetRequestValidator : AssetFieldsValidator<SaveAssetRequest>
{
    public SaveAssetRequestValidator()
    {
        RuleFor(r => r.Status)
            .Cascade(CascadeMode.Stop)
            .Must(EnumNames.IsDefined<AssetStatus>).WithMessage("Durum geçersiz.")
            .Must(status => EnumNames.Parse<AssetStatus>(status) != AssetStatus.Assigned)
            .WithMessage(AssetMessages.StatusAssignedOnlyByAssignment)
            .When(r => r.Status is not null);
    }
}

/// <summary>
/// "Assigned" passes here: whether it is allowed depends on the asset (it is when the asset is already assigned),
/// so <see cref="IAssetStore.UpdateAsync"/> checks it.
/// </summary>
internal sealed class UpdateAssetRequestValidator : AssetFieldsValidator<UpdateAssetRequest>
{
    public UpdateAssetRequestValidator()
    {
        RuleFor(r => r.Status)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Durum zorunludur.")
            .Must(EnumNames.IsDefined<AssetStatus>).WithMessage("Durum geçersiz.");

        RuleFor(r => r.RowVersion).MustBeRowVersion();
    }
}
