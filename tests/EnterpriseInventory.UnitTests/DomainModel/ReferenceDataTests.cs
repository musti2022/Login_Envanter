using EnterpriseInventory.Domain.Catalog;
using EnterpriseInventory.Domain.Common;
using EnterpriseInventory.Domain.Organization;

namespace EnterpriseInventory.UnitTests.DomainModel;

public class ReferenceDataTests
{
    [Fact]
    public void Lookups_are_created_active_with_trimmed_names()
    {
        var brand = Brand.Create("  Dell ");

        Assert.Equal("Dell", brand.Name);
        Assert.True(brand.IsActive);
    }

    [Fact]
    public void Lookups_can_be_renamed_deactivated_and_reactivated()
    {
        var department = Department.Create("Bilgi İşlem");

        department.Rename("Bilgi Teknolojileri");
        department.Deactivate();
        Assert.False(department.IsActive);

        department.Activate();
        Assert.True(department.IsActive);
        Assert.Equal("Bilgi Teknolojileri", department.Name);
    }

    [Fact]
    public void Lookup_names_are_required_and_limited()
    {
        DomainAssert.Violates(DomainErrors.Required, () => City.Create(" "));
        DomainAssert.Violates(DomainErrors.TooLong, () => City.Create(new string('x', ReferenceDataEntity.NameMaxLength + 1)));

        var city = City.Create("Bursa");
        DomainAssert.Violates(DomainErrors.Required, () => city.Rename(""));
        Assert.Equal("Bursa", city.Name);
    }

    [Fact]
    public void A_model_belongs_to_its_brand()
    {
        var brand = Brand.Create("HP");

        var model = AssetModel.Create(brand, "ProBook 450");

        Assert.Same(brand, model.Brand);
    }

    [Fact]
    public void A_location_belongs_to_its_city()
    {
        var city = City.Create("Ankara");

        var location = Location.Create(city, "Çankaya Ofis");

        Assert.Same(city, location.City);
        Assert.True(location.IsIn(city));
        Assert.False(location.IsIn(City.Create("Ankara")));
    }
}
