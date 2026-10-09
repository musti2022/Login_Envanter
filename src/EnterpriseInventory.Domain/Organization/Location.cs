using EnterpriseInventory.Domain.Common;

namespace EnterpriseInventory.Domain.Organization;

/// <summary>A physical place (building, floor, office) inside a city.</summary>
public sealed class Location : ReferenceDataEntity
{
    private Location()
    {
    }

    private Location(City city, string name)
        : base(name)
    {
        City = city;
        CityId = city.Id;
    }

    public int CityId { get; private set; }

    public City City { get; private set; } = null!;

    public static Location Create(City city, string name)
    {
        ArgumentNullException.ThrowIfNull(city);
        return new Location(city, name);
    }

    /// <summary>True when this location is in <paramref name="city"/>, comparing by key once saved and by reference before.</summary>
    public bool IsIn(City city)
    {
        ArgumentNullException.ThrowIfNull(city);
        return ReferenceEquals(City, city) || (CityId != 0 && CityId == city.Id);
    }
}
