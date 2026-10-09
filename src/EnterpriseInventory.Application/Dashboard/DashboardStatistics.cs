using EnterpriseInventory.Domain.Auditing;

namespace EnterpriseInventory.Application.Dashboard;

/// <summary>
/// What the dashboard shows. Counts are of assets that are not archived, except <see cref="ArchivedCount"/>;
/// the status counts add up to <see cref="TotalCount"/>.
/// </summary>
public sealed record DashboardStatistics(
    int TotalCount,
    int AssignedCount,
    int AvailableCount,
    int FaultyCount,
    int RetiredCount,
    int ArchivedCount,
    IReadOnlyList<DistributionItem> ByCity,
    IReadOnlyList<DistributionItem> ByDepartment,
    IReadOnlyList<RecentActivity> RecentActivity);

/// <summary>A city or department with how many assets are there, largest first.</summary>
public sealed record DistributionItem(int Id, string Name, int Count);

/// <summary>An asset audit record, with the asset's current code.</summary>
public sealed record RecentActivity(long Id, int AssetId, string? AssetCode, AuditAction Action, string UserName, DateTimeOffset Timestamp);

/// <summary>Reads the dashboard figures from the database.</summary>
public interface IDashboardStore
{
    /// <summary>How many records <see cref="DashboardStatistics.RecentActivity"/> holds.</summary>
    const int RecentActivityCount = 10;

    Task<DashboardStatistics> GetStatisticsAsync(CancellationToken cancellationToken);
}
