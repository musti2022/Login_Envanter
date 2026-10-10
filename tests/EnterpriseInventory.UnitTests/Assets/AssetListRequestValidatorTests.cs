using EnterpriseInventory.Application;
using EnterpriseInventory.Application.Assets;
using EnterpriseInventory.Domain.Assets;
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

    [Fact]
    public void Every_filter_and_sort_given_by_name_is_accepted()
    {
        var request = new AssetListRequest
        {
            Search = "dell  izmir",
            Status = ["available", "Assigned"],
            AssetType = ["LAPTOP"],
            BrandId = 1,
            ModelId = 2,
            CityId = 3,
            DepartmentId = 4,
            LocationId = 5,
            Archived = true,
            SortBy = "assignedDisplayName",
            SortDirection = "DESC",
        };

        Assert.True(_validator.Validate(request).IsValid);
    }

    [Theory]
    [InlineData("bir iki üç dört beş", true)]
    [InlineData("bir iki üç dört beş altı", false)]
    [InlineData("aynı aynı aynı aynı aynı aynı", true)]
    [InlineData("bir\tiki", false)]
    public void A_search_has_at_most_five_different_words_and_no_control_characters(string search, bool valid) =>
        Assert.Equal(valid, _validator.Validate(new AssetListRequest { Search = search }).IsValid);

    [Theory]
    [InlineData("Status", "Available,Faulty")]
    [InlineData("Status", "1")]
    [InlineData("AssetType", "")]
    [InlineData("SortBy", "Id")]
    [InlineData("SortBy", "assetCode desc")]
    [InlineData("SortDirection", "descending")]
    public void Unknown_names_are_refused(string field, string value)
    {
        var request = field switch
        {
            "Status" => new AssetListRequest { Status = ["Available", value] },
            "AssetType" => new AssetListRequest { AssetType = [value] },
            "SortBy" => new AssetListRequest { SortBy = value },
            _ => new AssetListRequest { SortDirection = value },
        };

        Assert.Equal(field, Assert.Single(_validator.Validate(request).Errors).PropertyName);
    }

    [Fact]
    public void The_criteria_parse_the_names_and_drop_repeated_values()
    {
        var criteria = AssetListCriteria.From(new AssetListRequest
        {
            Search = "  Dell   İzmir dell Dell ",
            Status = ["faulty", "Faulty", "retired"],
            AssetType = ["desktop"],
            SortBy = "cityname",
            SortDirection = "Desc",
        });

        Assert.Equal(["Dell", "İzmir", "dell"], criteria.SearchTerms);
        Assert.Equal([AssetStatus.Faulty, AssetStatus.Retired], criteria.Statuses);
        Assert.Equal([AssetType.Desktop], criteria.AssetTypes);
        Assert.Equal(AssetSortField.CityName, criteria.SortBy);
        Assert.True(criteria.Descending);
        Assert.False(criteria.Archived);
    }

    [Fact]
    public void Without_parameters_the_first_page_of_active_assets_is_listed_by_code()
    {
        var criteria = AssetListCriteria.From(new AssetListRequest());

        Assert.Equal((1, AssetListRequest.DefaultPageSize), (criteria.Page, criteria.PageSize));
        Assert.Empty(criteria.SearchTerms);
        Assert.Empty(criteria.Statuses);
        Assert.False(criteria.Archived);
        Assert.Equal(AssetSortField.AssetCode, criteria.SortBy);
        Assert.False(criteria.Descending);
    }
}
