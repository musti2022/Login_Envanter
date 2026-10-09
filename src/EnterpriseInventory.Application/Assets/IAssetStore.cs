namespace EnterpriseInventory.Application.Assets;

/// <summary>Reads and writes assets in the database. Callers pass input that <see cref="AssetService"/> has validated.</summary>
public interface IAssetStore
{
    /// <summary>Assets that are not archived, ordered by asset code, one page at a time.</summary>
    Task<PagedResult<AssetListItem>> ListAsync(AssetListCriteria criteria, CancellationToken cancellationToken);

    /// <summary>The asset, archived or not, or <c>null</c> when there is none with that ID.</summary>
    Task<AssetDetails?> FindAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Adds the asset with a <c>Created</c> audit record in one transaction. Missing or inactive lookups and
    /// duplicate codes or serial numbers are refused with field errors.
    /// </summary>
    Task<AssetWriteResult> CreateAsync(AssetDraft draft, CancellationToken cancellationToken);
}
