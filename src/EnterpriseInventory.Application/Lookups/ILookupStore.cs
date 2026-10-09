namespace EnterpriseInventory.Application.Lookups;

/// <summary>
/// Reads and creates brands, models, cities, locations and departments. Lists are sorted by name (Turkish rules)
/// and include inactive lookups. A create also writes its audit record, in the same transaction.
/// </summary>
public interface ILookupStore
{
    Task<IReadOnlyList<LookupItem>> ListAsync(LookupKind kind, CancellationToken cancellationToken);

    /// <param name="brandId">Only this brand's models; every model when <c>null</c>.</param>
    Task<IReadOnlyList<ModelItem>> ListModelsAsync(int? brandId, CancellationToken cancellationToken);

    /// <param name="cityId">Only this city's locations; every location when <c>null</c>.</param>
    Task<IReadOnlyList<LocationItem>> ListLocationsAsync(int? cityId, CancellationToken cancellationToken);

    /// <param name="name">Trimmed and validated.</param>
    Task<LookupWriteResult<LookupItem>> CreateAsync(LookupKind kind, string name, CancellationToken cancellationToken);

    /// <summary>Refused when the brand is missing or inactive.</summary>
    Task<LookupWriteResult<ModelItem>> CreateModelAsync(int brandId, string name, CancellationToken cancellationToken);

    /// <summary>Refused when the city is missing or inactive.</summary>
    Task<LookupWriteResult<LocationItem>> CreateLocationAsync(int cityId, string name, CancellationToken cancellationToken);
}
