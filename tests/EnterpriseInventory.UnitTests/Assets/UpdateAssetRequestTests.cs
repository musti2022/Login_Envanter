using EnterpriseInventory.Application;
using EnterpriseInventory.Application.Assets;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseInventory.UnitTests.Assets;

public class UpdateAssetRequestTests
{
    private static readonly string CurrentVersion = Convert.ToBase64String([0, 0, 0, 0, 0, 0, 0x1F, 0x4A]);

    private static readonly UpdateAssetRequest Valid = new()
    {
        AssetCode = "DMR-1",
        AssetType = "Laptop",
        Status = "Available",
        ModelId = 1,
        CityId = 2,
        DepartmentId = 3,
        RowVersion = CurrentVersion,
    };

    private readonly IValidator<UpdateAssetRequest> _validator =
        new ServiceCollection().AddApplication().BuildServiceProvider().GetRequiredService<IValidator<UpdateAssetRequest>>();

    [Fact]
    public void A_complete_update_with_the_row_version_is_valid() =>
        Assert.True(_validator.Validate(Valid).IsValid);

    [Theory]
    [InlineData(null, "Kayıt sürümü (rowVersion) zorunludur.")]
    [InlineData("  ", "Kayıt sürümü (rowVersion) zorunludur.")]
    [InlineData("not-base64!!", "Kayıt sürümü (rowVersion) geçersiz; kaydı yeniden açıp tekrar deneyin.")]
    [InlineData("AAAAAAAAH0o", "Kayıt sürümü (rowVersion) geçersiz; kaydı yeniden açıp tekrar deneyin.")]
    [InlineData("AAAAAAAAAAAfSg==", "Kayıt sürümü (rowVersion) geçersiz; kaydı yeniden açıp tekrar deneyin.")]
    [InlineData("AAAAAA==", "Kayıt sürümü (rowVersion) geçersiz; kaydı yeniden açıp tekrar deneyin.")]
    public void The_row_version_is_required_and_must_be_eight_bytes_of_base64(string? rowVersion, string message)
    {
        var result = _validator.Validate(Valid with { RowVersion = rowVersion });

        Assert.Equal(message, Assert.Single(result.Errors, e => e.PropertyName == nameof(UpdateAssetRequest.RowVersion)).ErrorMessage);
    }

    [Theory]
    [InlineData(null, "Durum zorunludur.")]
    [InlineData("", "Durum zorunludur.")]
    [InlineData("Kayıp", "Durum geçersiz.")]
    public void The_status_is_required(string? status, string message)
    {
        var result = _validator.Validate(Valid with { Status = status });

        Assert.Equal(message, Assert.Single(result.Errors).ErrorMessage);
    }

    [Fact]
    public void Assigned_passes_validation_because_only_the_asset_knows_whether_it_is_allowed() =>
        Assert.True(_validator.Validate(Valid with { Status = "Assigned" }).IsValid);

    [Fact]
    public void Field_rules_are_the_same_as_when_creating()
    {
        var result = _validator.Validate(Valid with { AssetCode = " ", ModelId = 0, LocationId = -1 });

        Assert.Equal(
            ["AssetCode", "LocationId", "ModelId"],
            result.Errors.Select(e => e.PropertyName).Order(StringComparer.Ordinal));
    }
}
