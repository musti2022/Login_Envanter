namespace EnterpriseInventory.Application.Assets;

public enum AssetWriteOutcome
{
    Succeeded = 0,

    /// <summary>The input is wrong; <see cref="AssetWriteResult.Errors"/> says which fields and why.</summary>
    ValidationFailed = 1,

    NotFound = 2,

    /// <summary>The asset code or serial number belongs to another asset; <see cref="AssetWriteResult.Errors"/> names the field.</summary>
    DuplicateValue = 3,

    /// <summary>Someone else changed the asset since the caller read it, so nothing was written.</summary>
    ConcurrencyConflict = 4,

    /// <summary>A business rule refused the change; <see cref="AssetWriteResult.RuleCode"/> is a <c>DomainErrors</c> code.</summary>
    RuleViolated = 5,

    /// <summary>The directory, which had to confirm the employee, could not be reached; nothing was written.</summary>
    DirectoryUnavailable = 6,
}

/// <summary>What happened to a create, update, archive, assignment, return or move. <see cref="Asset"/> is the asset as saved.</summary>
public sealed record AssetWriteResult(
    AssetWriteOutcome Outcome,
    AssetDetails? Asset = null,
    IDictionary<string, string[]>? Errors = null,
    string? RuleCode = null)
{
    public static AssetWriteResult Succeeded(AssetDetails? asset) => new(AssetWriteOutcome.Succeeded, asset);

    public static AssetWriteResult Invalid(IDictionary<string, string[]> errors) => new(AssetWriteOutcome.ValidationFailed, Errors: errors);

    public static AssetWriteResult Duplicate(IDictionary<string, string[]> errors) => new(AssetWriteOutcome.DuplicateValue, Errors: errors);

    public static AssetWriteResult NotFound { get; } = new(AssetWriteOutcome.NotFound);

    public static AssetWriteResult Conflict { get; } = new(AssetWriteOutcome.ConcurrencyConflict);

    public static AssetWriteResult DirectoryUnavailable { get; } = new(AssetWriteOutcome.DirectoryUnavailable);

    public static AssetWriteResult Rule(string code) => new(AssetWriteOutcome.RuleViolated, RuleCode: code);
}

/// <summary>Turkish field messages for checks that need the database or the asset, shared by every <see cref="IAssetStore"/>.</summary>
public static class AssetMessages
{
    public const string ModelNotFound = "Seçilen model bulunamadı.";
    public const string ModelInactive = "Seçilen model pasif; yeni seçimlerde kullanılamaz.";
    public const string BrandInactive = "Seçilen modelin markası pasif; yeni seçimlerde kullanılamaz.";
    public const string CityNotFound = "Seçilen şehir bulunamadı.";
    public const string CityInactive = "Seçilen şehir pasif; yeni seçimlerde kullanılamaz.";
    public const string DepartmentNotFound = "Seçilen departman bulunamadı.";
    public const string DepartmentInactive = "Seçilen departman pasif; yeni seçimlerde kullanılamaz.";
    public const string LocationNotFound = "Seçilen lokasyon bulunamadı.";
    public const string LocationInactive = "Seçilen lokasyon pasif; yeni seçimlerde kullanılamaz.";
    public const string LocationInAnotherCity = "Seçilen lokasyon seçilen şehirde değil.";
    public const string AssetCodeTaken = "Bu demirbaş kodu başka bir kayıtta kullanılıyor.";
    public const string SerialNumberTaken = "Bu seri numarası başka bir kayıtta kullanılıyor.";
    public const string StatusAssignedOnlyByAssignment = "Zimmetli durumu yalnızca demirbaş zimmetlenerek verilir.";
}
