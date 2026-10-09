namespace EnterpriseInventory.Application.Assets;

/// <summary>Reads and writes assets in the database. Callers pass input that <see cref="AssetService"/> has validated.</summary>
public interface IAssetStore
{
    /// <summary>
    /// The assets that match the criteria (by default: not archived, by asset code), one page at a time. Ties are
    /// broken by asset code and ID, so every asset appears on exactly one page.
    /// </summary>
    Task<PagedResult<AssetListItem>> ListAsync(AssetListCriteria criteria, CancellationToken cancellationToken);

    /// <summary>The asset, archived or not, or <c>null</c> when there is none with that ID.</summary>
    Task<AssetDetails?> FindAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Adds the asset with a <c>Created</c> audit record in one transaction. Missing or inactive lookups and
    /// duplicate codes or serial numbers are refused with field errors.
    /// </summary>
    Task<AssetWriteResult> CreateAsync(AssetDraft draft, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the asset's fields with <paramref name="draft"/>, with one audit record per kind of change, in one
    /// transaction. Refused with <see cref="AssetWriteOutcome.ConcurrencyConflict"/> when the asset is no longer
    /// at <paramref name="rowVersion"/>, so nobody silently overwrites someone else's edit. Nothing to change,
    /// nothing written.
    /// </summary>
    Task<AssetWriteResult> UpdateAsync(int id, AssetDraft draft, byte[] rowVersion, CancellationToken cancellationToken);

    /// <summary>
    /// Archives (soft-deletes) the asset with an <c>Archived</c> audit record. The row, its assignment history and
    /// its audit records stay; the asset leaves the inventory list but can still be read. Same version check as
    /// <see cref="UpdateAsync"/>.
    /// </summary>
    Task<AssetWriteResult> ArchiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken);

    /// <summary>The asset's audit records, newest first, or <c>null</c> when there is no asset with that ID.</summary>
    Task<PagedResult<AssetHistoryEntry>?> HistoryAsync(int id, int page, int pageSize, CancellationToken cancellationToken);
}
