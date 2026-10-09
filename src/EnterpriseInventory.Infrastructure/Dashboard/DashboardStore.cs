using System.Globalization;
using EnterpriseInventory.Application.Dashboard;
using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Infrastructure.Assets;
using EnterpriseInventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseInventory.Infrastructure.Dashboard;

internal sealed class DashboardStore(ApplicationDbContext db) : IDashboardStore
{
    public async Task<DashboardStatistics> GetStatisticsAsync(CancellationToken cancellationToken)
    {
        // Archived assets are counted too (ArchivedCount), so this query turns the soft-delete filter off.
        var byStatus = await db.Assets.IncludingArchived().AsNoTracking()
            .GroupBy(a => new { a.IsDeleted, a.Status })
            .Select(g => new { g.Key.IsDeleted, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int Count(AssetStatus status) => byStatus.Where(g => !g.IsDeleted && g.Status == status).Sum(g => g.Count);

        var active = db.Assets.AsNoTracking();
        var byCity = await active
            .GroupBy(a => new { a.City.Id, a.City.Name })
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.Name)
            .Select(g => new DistributionItem(g.Key.Id, g.Key.Name, g.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var byDepartment = await active
            .GroupBy(a => new { a.Department.Id, a.Department.Name })
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.Name)
            .Select(g => new DistributionItem(g.Key.Id, g.Key.Name, g.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new DashboardStatistics(
            byStatus.Where(g => !g.IsDeleted).Sum(g => g.Count),
            Count(AssetStatus.Assigned),
            Count(AssetStatus.Available),
            Count(AssetStatus.Faulty),
            Count(AssetStatus.Retired),
            byStatus.Where(g => g.IsDeleted).Sum(g => g.Count),
            byCity,
            byDepartment,
            await RecentActivityAsync(cancellationToken).ConfigureAwait(false));
    }

    private async Task<IReadOnlyList<RecentActivity>> RecentActivityAsync(CancellationToken cancellationToken)
    {
        var records = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityName == AssetAuditTrail.EntityName)
            .OrderByDescending(a => a.Id)
            .Take(IDashboardStore.RecentActivityCount)
            .Select(a => new { a.Id, a.EntityId, a.Action, a.UserName, a.Timestamp })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var assetIds = records.Select(r => int.Parse(r.EntityId, CultureInfo.InvariantCulture)).Distinct().ToList();
        // Recent activity includes archiving, so archived assets are named too.
        var codes = await db.Assets.IncludingArchived().AsNoTracking()
            .Where(a => assetIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.AssetCode, cancellationToken)
            .ConfigureAwait(false);

        return records.ConvertAll(r =>
        {
            var assetId = int.Parse(r.EntityId, CultureInfo.InvariantCulture);
            return new RecentActivity(r.Id, assetId, codes.GetValueOrDefault(assetId), r.Action, r.UserName, r.Timestamp);
        });
    }
}
