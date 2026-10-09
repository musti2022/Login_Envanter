using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseInventory.Infrastructure.Assets;

internal static class AssetVersions
{
    /// <summary>
    /// Whether the caller read the version just loaded; refusing here spares the work of a change that would fail.
    /// The client's version also becomes the original value EF Core puts in the UPDATE's WHERE, so a change that
    /// lands between this read and the save is caught by the database (<see cref="DbUpdateConcurrencyException"/>)
    /// and nothing is overwritten.
    /// </summary>
    public static bool MatchesClientVersion(this ApplicationDbContext db, Asset asset, byte[] rowVersion)
    {
        db.Entry(asset).Property(a => a.RowVersion).OriginalValue = rowVersion;
        return asset.RowVersion.AsSpan().SequenceEqual(rowVersion);
    }
}
