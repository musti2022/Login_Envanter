using EnterpriseInventory.Application;
using EnterpriseInventory.Application.Assets;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseInventory.UnitTests.Assets;

public class AssetListRequestValidatorTests
{
    private readonly IValidator<AssetListRequest> _validator =
        new ServiceCollection().AddApplication().BuildServiceProvider().GetRequiredService<IValidator<AssetListRequest>>();

    [Theory]
    [InlineData(null, null)]
    [InlineData(1, 1)]
    [InlineData(100_000, 100)]
    public void Paging_inside_the_limits_is_accepted(int? page, int? pageSize) =>
        Assert.True(_validator.Validate(new AssetListRequest { Page = page, PageSize = pageSize }).IsValid);

    [Theory]
    [InlineData(0, null, "Page")]
    [InlineData(100_001, null, "Page")]
    [InlineData(null, 0, "PageSize")]
    [InlineData(null, 101, "PageSize")]
    public void Paging_outside_the_limits_names_the_field(int? page, int? pageSize, string field)
    {
        var result = _validator.Validate(new AssetListRequest { Page = page, PageSize = pageSize });

        Assert.Equal(field, Assert.Single(result.Errors).PropertyName);
    }
}
