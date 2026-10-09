namespace EnterpriseInventory.Application.Lookups;

/// <summary>The lookups that have nothing above them; models and locations belong to a brand and a city.</summary>
public enum LookupKind
{
    Brand = 0,
    City,
    Department,
}

/// <summary>A brand, city or department. Inactive ones are listed too, so screens can still name them.</summary>
public sealed record LookupItem(int Id, string Name, bool IsActive);

/// <summary>A model and the brand it belongs to.</summary>
public sealed record ModelItem(int Id, string Name, bool IsActive, int BrandId, string BrandName);

/// <summary>A location and the city it is in.</summary>
public sealed record LocationItem(int Id, string Name, bool IsActive, int CityId, string CityName);
