using EnterpriseInventory.Application;
using EnterpriseInventory.Application.Assets;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseInventory.UnitTests.Assets;

/// <summary>The field rules of a move (day 25); whether the values exist and fit together is the store's check.</summary>
public class ChangeAssetLocationRequestTests
{
    private readonly IValidator<ChangeAssetLocationRequest> _validator =
        new ServiceCollection().AddApplication().BuildServiceProvider().GetRequiredService<IValidator<ChangeAssetLocationRequest>>();

    [Theory]
    [InlineData(null)]
    [InlineData(5)]
    public void A_city_and_a_department_are_enough_and_the_location_is_optional(int? locationId) =>
        Assert.True(_validator.Validate(new ChangeAssetLocationRequest { CityId = 1, DepartmentId = 2, LocationId = locationId, RowVersion = "AAAAAAAAB9E=" }).IsValid);

    [Fact]
    public void Each_missing_or_invalid_value_is_named_in_Turkish()
    {
        var result = _validator.Validate(new ChangeAssetLocationRequest { CityId = null, DepartmentId = 0, LocationId = -3, RowVersion = "x" });

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["CityId"] = "Şehir seçilmelidir.",
                ["DepartmentId"] = "Departman geçersiz.",
                ["LocationId"] = "Lokasyon geçersiz.",
                ["RowVersion"] = "Kayıt sürümü (rowVersion) geçersiz; kaydı yeniden açıp tekrar deneyin.",
            },
            result.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage));
    }
}
