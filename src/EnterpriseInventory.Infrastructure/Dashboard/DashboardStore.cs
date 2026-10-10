using System.Globalization;
using EnterpriseInventory.Application.Dashboard;
using EnterpriseInventory.Application.Exports;
using EnterpriseInventory.Application.Reports;
using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Infrastructure.Assets;
using EnterpriseInventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EnterpriseInventory.Infrastructure.Dashboard;

internal sealed class DashboardStore(ApplicationDbContext db, TimeProvider timeProvider, IOptions<ReportingOptions> reporting) : IDashboardStore
{
    private static readonly StringComparer TurkishOrder = StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: false);

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
            .Select(g => new DistributionItem(g.Key.Id, g.Key.Name, g.Count(), g.Count(a => a.Status == AssetStatus.Assigned)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var byDepartment = await active
            .GroupBy(a => new { a.Department.Id, a.Department.Name })
            .Select(g => new DistributionItem(g.Key.Id, g.Key.Name, g.Count(), g.Count(a => a.Status == AssetStatus.Assigned)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var byBrand = await active
            .GroupBy(a => new { a.Brand.Id, a.Brand.Name })
            .Select(g => new DistributionItem(g.Key.Id, g.Key.Name, g.Count(), g.Count(a => a.Status == AssetStatus.Assigned)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var byType = await active
            .GroupBy(a => a.AssetType)
            .Select(g => new TypeCount(g.Key, g.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new DashboardStatistics(
            byStatus.Where(g => !g.IsDeleted).Sum(g => g.Count),
            Count(AssetStatus.Assigned),
            Count(AssetStatus.Available),
            Count(AssetStatus.Faulty),
            Count(AssetStatus.Retired),
            byStatus.Where(g => g.IsDeleted).Sum(g => g.Count),
            Largest(byCity),
            Largest(byDepartment),
            [.. byType.OrderByDescending(t => t.Count).ThenBy(t => t.AssetType)],
            Largest(byBrand),
            await MonthlyMovementsAsync(cancellationToken).ConfigureAwait(false),
            await RecentActivityAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>The largest groups, names in Turkish alphabetical order breaking ties, and the rest together.</summary>
    private static Distribution Largest(List<DistributionItem> items)
    {
        var ordered = items.OrderByDescending(i => i.Count).ThenBy(i => i.Name, TurkishOrder).ToList();
        var rest = ordered.Skip(IDashboardStore.TopGroupCount).ToList();
        return new Distribution(
            [.. ordered.Take(IDashboardStore.TopGroupCount)], rest.Count, rest.Sum(i => i.Count), rest.Sum(i => i.AssignedCount));
    }

    /// <summary>
    /// Assignments and returns per month of the reporting time zone. The month boundaries are local midnights, so
    /// an assignment at 23:30 on the last day of a month in Istanbul counts in that month whatever its UTC date.
    /// </summary>
    private async Task<IReadOnlyList<MonthlyMovement>> MonthlyMovementsAsync(CancellationToken cancellationToken)
    {
        var zone = reporting.Value.ResolveTimeZone();
        var localNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone);
        var months = Enumerable.Range(0, IDashboardStore.MovementMonths)
            .Select(i => new DateTime(localNow.Year, localNow.Month, 1).AddMonths(i - IDashboardStore.MovementMonths + 1))
            .ToList();
        var from = ReportingTime.ToMoment(months[0], zone);

        // Every assignment of the window, archived assets included: those movements happened.
        var assignments = db.AssetAssignments.IncludingArchived().AsNoTracking();
        var assigned = await assignments
            .Where(x => x.AssignedAt >= from)
            .Select(x => x.AssignedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var returned = await assignments
            .Where(x => x.ReturnedAt != null && x.ReturnedAt >= from)
            .Select(x => x.ReturnedAt!.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        string MonthOf(DateTimeOffset moment) =>
            TimeZoneInfo.ConvertTime(moment, zone).ToString("yyyy-MM", CultureInfo.InvariantCulture);

        var assignedByMonth = assigned.CountBy(MonthOf).ToDictionary();
        var returnedByMonth = returned.CountBy(MonthOf).ToDictionary();
        return months.ConvertAll(month =>
        {
            var key = month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            return new MonthlyMovement(key, assignedByMonth.GetValueOrDefault(key), returnedByMonth.GetValueOrDefault(key));
        });
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
            .Select(a => new { a.Id, a.AssetCode })
            .ToDictionaryAsync(a => a.Id, a => a.AssetCode, cancellationToken)
            .ConfigureAwait(false);

        return records.ConvertAll(r =>
        {
            var assetId = int.Parse(r.EntityId, CultureInfo.InvariantCulture);
            return new RecentActivity(r.Id, assetId, codes.GetValueOrDefault(assetId), r.Action, r.UserName, r.Timestamp);
        });
    }
}
