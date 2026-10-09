using FluentValidation;

namespace EnterpriseInventory.Application.Assets;

/// <summary>The outcome of a list request: a page, or the reasons the request was refused.</summary>
public sealed record AssetListResult(PagedResult<AssetListItem>? Page, IDictionary<string, string[]>? Errors)
{
    public bool IsValid => Page is not null;
}

/// <summary>Inventory use cases: checks the caller's input and hands it to <see cref="IAssetStore"/>.</summary>
public sealed class AssetService(
    IValidator<AssetListRequest> listValidator,
    IValidator<SaveAssetRequest> saveValidator,
    IValidator<UpdateAssetRequest> updateValidator,
    IAssetStore store)
{
    public async Task<AssetListResult> ListAsync(AssetListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await listValidator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return new AssetListResult(null, validation.ToDictionary());
        }

        var criteria = new AssetListCriteria(request.Page ?? 1, request.PageSize ?? AssetListRequest.DefaultPageSize);
        return new AssetListResult(await store.ListAsync(criteria, cancellationToken).ConfigureAwait(false), null);
    }

    public Task<AssetDetails?> FindAsync(int id, CancellationToken cancellationToken) => store.FindAsync(id, cancellationToken);

    public async Task<AssetWriteResult> CreateAsync(SaveAssetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await saveValidator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        return validation.IsValid
            ? await store.CreateAsync(AssetDraft.From(request), cancellationToken).ConfigureAwait(false)
            : AssetWriteResult.Invalid(validation.ToDictionary());
    }

    public async Task<AssetWriteResult> UpdateAsync(int id, UpdateAssetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await updateValidator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        return validation.IsValid
            ? await store.UpdateAsync(id, AssetDraft.From(request), AssetRowVersion.Decode(request.RowVersion), cancellationToken).ConfigureAwait(false)
            : AssetWriteResult.Invalid(validation.ToDictionary());
    }
}
