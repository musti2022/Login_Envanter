using EnterpriseInventory.Application;
using EnterpriseInventory.Application.Assets;
using EnterpriseInventory.Domain.Assets;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseInventory.UnitTests.Assets;

public class SaveAssetRequestTests
{
    private static readonly SaveAssetRequest Valid = new()
    {
        AssetCode = "DMR-1",
        AssetType = "Laptop",
        ModelId = 1,
        CityId = 2,
        DepartmentId = 3,
    };

    private readonly IValidator<SaveAssetRequest> _validator =
        new ServiceCollection().AddApplication().BuildServiceProvider().GetRequiredService<IValidator<SaveAssetRequest>>();

    [Fact]
    public void Only_the_code_type_model_city_and_department_are_required() =>
        Assert.True(_validator.Validate(Valid).IsValid);

    [Theory]
    [InlineData("Laptop", true)]
    [InlineData("laptop", true)]
    [InlineData("OTHER", true)]
    [InlineData("2", false)]
    [InlineData("-1", false)]
    [InlineData("99", false)]
    [InlineData("Laptop, Desktop", false)]
    [InlineData("Laptop,Desktop", false)]
    [InlineData(" Laptop", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Asset_types_are_accepted_by_name_only(string? name, bool valid) =>
        Assert.Equal(valid, _validator.Validate(Valid with { AssetType = name }).IsValid);

    [Theory]
    [InlineData("Available", true)]
    [InlineData("faulty", true)]
    [InlineData("Retired", true)]
    [InlineData("Assigned", false)]
    [InlineData("3", false)]
    public void A_status_may_be_given_but_never_assigned(string status, bool valid) =>
        Assert.Equal(valid, _validator.Validate(Valid with { Status = status }).IsValid);

    [Fact]
    public void Lengths_are_measured_after_trimming()
    {
        var padded = Valid with { AssetCode = $"  {new string('x', Asset.AssetCodeMaxLength)}  " };

        Assert.True(_validator.Validate(padded).IsValid);
        Assert.False(_validator.Validate(Valid with { AssetCode = new string('x', Asset.AssetCodeMaxLength + 1) }).IsValid);
    }

    [Fact]
    public void The_draft_has_trimmed_text_parsed_enums_a_normalized_serial_number_and_no_empty_strings()
    {
        var draft = AssetDraft.From(Valid with
        {
            AssetCode = " DMR-1 ",
            AssetType = "desktop",
            Status = "faulty",
            ComputerName = "  ",
            SerialNumber = " sn 1 ",
            Description = "",
            LocationId = 4,
        });

        Assert.Equal(
            new AssetDraft("DMR-1", AssetType.Desktop, AssetStatus.Faulty, 1, 2, 3, 4, null, "SN1", null),
            draft);
    }

    [Fact]
    public void Without_a_status_the_draft_keeps_the_current_one() =>
        Assert.Null(AssetDraft.From(Valid).Status);
}
