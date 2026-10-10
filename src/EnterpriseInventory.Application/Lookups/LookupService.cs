using EnterpriseInventory.Application.Assets;
using FluentValidation;

namespace EnterpriseInventory.Application.Lookups;

/// <summary>Lookup use cases: checks the caller's input and hands it to <see cref="ILookupStore"/>.</summary>
public sealed class LookupService(
    IValidator<CreateLookupRequest> lookupValidator,
    IValidator<CreateModelRequest> modelValidator,
    IValidator<CreateLocationRequest> locationValidator,
    IValidator<UpdateLookupRequest> updateValidator,
    ILookupStore store)
{
    public Task<IReadOnlyList<LookupItem>> ListAsync(LookupKind kind, CancellationToken cancellationToken) => store.ListAsync(kind, cancellationToken);

    public Task<IReadOnlyList<ModelItem>> ListModelsAsync(int? brandId, CancellationToken cancellationToken) =>
        store.ListModelsAsync(brandId, cancellationToken);

    public Task<IReadOnlyList<LocationItem>> ListLocationsAsync(int? cityId, CancellationToken cancellationToken) =>
        store.ListLocationsAsync(cityId, cancellationToken);

    public async Task<LookupWriteResult<LookupItem>> CreateAsync(LookupKind kind, CreateLookupRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await lookupValidator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        return validation.IsValid
            ? await store.CreateAsync(kind, request.Name!.Trim(), cancellationToken).ConfigureAwait(false)
            : LookupWriteResult.Invalid<LookupItem>(validation.ToDictionary());
    }

    public async Task<LookupWriteResult<ModelItem>> CreateModelAsync(CreateModelRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await modelValidator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        return validation.IsValid
            ? await store.CreateModelAsync(request.BrandId!.Value, request.Name!.Trim(), cancellationToken).ConfigureAwait(false)
            : LookupWriteResult.Invalid<ModelItem>(validation.ToDictionary());
    }

    public async Task<LookupWriteResult<LocationItem>> CreateLocationAsync(CreateLocationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await locationValidator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        return validation.IsValid
            ? await store.CreateLocationAsync(request.CityId!.Value, request.Name!.Trim(), cancellationToken).ConfigureAwait(false)
            : LookupWriteResult.Invalid<LocationItem>(validation.ToDictionary());
    }

    public Task<LookupWriteResult<LookupItem>> UpdateAsync(LookupKind kind, int id, UpdateLookupRequest request, CancellationToken cancellationToken) =>
        UpdateAsync(request, (name, isActive, rowVersion) => store.UpdateAsync(kind, id, name, isActive, rowVersion, cancellationToken), cancellationToken);

    public Task<LookupWriteResult<ModelItem>> UpdateModelAsync(int id, UpdateLookupRequest request, CancellationToken cancellationToken) =>
        UpdateAsync(request, (name, isActive, rowVersion) => store.UpdateModelAsync(id, name, isActive, rowVersion, cancellationToken), cancellationToken);

    public Task<LookupWriteResult<LocationItem>> UpdateLocationAsync(int id, UpdateLookupRequest request, CancellationToken cancellationToken) =>
        UpdateAsync(request, (name, isActive, rowVersion) => store.UpdateLocationAsync(id, name, isActive, rowVersion, cancellationToken), cancellationToken);

    private async Task<LookupWriteResult<T>> UpdateAsync<T>(
        UpdateLookupRequest request, Func<string, bool, byte[], Task<LookupWriteResult<T>>> update, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await updateValidator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        return validation.IsValid
            ? await update(request.Name!.Trim(), request.IsActive!.Value, AssetRowVersion.Decode(request.RowVersion)).ConfigureAwait(false)
            : LookupWriteResult.Invalid<T>(validation.ToDictionary());
    }
}
