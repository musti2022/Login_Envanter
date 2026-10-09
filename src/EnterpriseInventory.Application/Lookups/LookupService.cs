using FluentValidation;

namespace EnterpriseInventory.Application.Lookups;

/// <summary>Lookup use cases: checks the caller's input and hands it to <see cref="ILookupStore"/>.</summary>
public sealed class LookupService(
    IValidator<CreateLookupRequest> lookupValidator,
    IValidator<CreateModelRequest> modelValidator,
    IValidator<CreateLocationRequest> locationValidator,
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
}
