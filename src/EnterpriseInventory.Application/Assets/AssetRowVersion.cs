using FluentValidation;

namespace EnterpriseInventory.Application.Assets;

/// <summary>
/// The <c>rowVersion</c> callers send back to prove which version of an asset they edited: the base64 text of the
/// 8-byte SQL Server <c>rowversion</c> that <see cref="AssetDetails.RowVersion"/> gave them.
/// </summary>
internal static class AssetRowVersion
{
    private const int Length = 8;

    public static bool TryDecode(string? text, out byte[] bytes)
    {
        bytes = new byte[Length];
        return text is { Length: 12 } && Convert.TryFromBase64String(text, bytes, out var written) && written == Length;
    }

    public static byte[] Decode(string? text) =>
        TryDecode(text, out var bytes) ? bytes : throw new FormatException("The row version was not validated.");

    public static IRuleBuilderOptions<T, string?> MustBeRowVersion<T>(this IRuleBuilderInitial<T, string?> rule) =>
        rule.Cascade(CascadeMode.Stop)
            .Must(text => !string.IsNullOrWhiteSpace(text)).WithMessage("Kayıt sürümü (rowVersion) zorunludur.")
            .Must(text => TryDecode(text, out _)).WithMessage("Kayıt sürümü (rowVersion) geçersiz; kaydı yeniden açıp tekrar deneyin.");
}
