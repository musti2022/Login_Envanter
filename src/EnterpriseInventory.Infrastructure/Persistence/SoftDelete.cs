using EnterpriseInventory.Domain.Assets;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseInventory.Infrastructure.Persistence;

/// <summary>
/// Archiving is a soft delete: <see cref="Asset.IsDeleted"/> is set and the row stays. A global query filter named
/// <see cref="FilterName"/> hides archived assets, and the assignments of archived assets, from every query.
/// </summary>
internal static class SoftDelete
{
    public const string FilterName = "SoftDelete";

    /// <summary>
    /// The query with archived assets included. Only for queries that are about the archive or the history on
    /// purpose: the archived list, an asset's detail and history, the change that loads an asset to edit or
    /// archive it, uniqueness checks and statistics. This is the only place that turns the filter off.
    /// </summary>
    public static IQueryable<T> IncludingArchived<T>(this IQueryable<T> query)
        where T : class =>
        query.IgnoreQueryFilters([FilterName]);
}
