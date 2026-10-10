using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Domain.Auditing;

namespace EnterpriseInventory.Application.Dashboard;

/// <summary>
/// What the dashboard shows. Counts are of assets that are not archived, except <see cref="ArchivedCount"/> and the
/// monthly movements; the status counts add up to <see cref="TotalCount"/>, and so do the counts of every
/// distribution.
/// </summary>
/// <param name="MonthlyMovements">
/// The last <see cref="IDashboardStore.MovementMonths"/> months, oldest first, ending with the current month in the
/// reporting time zone; assets archived since count too, since the movements happened.
/// </param>
public sealed record DashboardStatistics(
    int TotalCount,
    int AssignedCount,
    int AvailableCount,
    int FaultyCount,
    int RetiredCount,
    int ArchivedCount,
    Distribution ByCity,
    Distribution ByDepartment,
    IReadOnlyList<TypeCount> ByType,
    Distribution ByBrand,
    IReadOnlyList<MonthlyMovement> MonthlyMovements,
    IReadOnlyList<RecentActivity> RecentActivity);

/// <summary>
/// The <see cref="IDashboardStore.TopGroupCount"/> cities, departments or brands with the most assets, largest first
/// (names in Turkish alphabetical order break ties), and every other one counted together, so that a company with
/// eighty cities still gets a short list. The full list is the grouped report.
/// </summary>
/// <param name="OtherGroupCount">How many cities, departments or brands are counted together.</param>
/// <param name="OtherCount">Their assets.</param>
/// <param name="OtherAssignedCount">Their assigned assets.</param>
public sealed record Distribution(IReadOnlyList<DistributionItem> Items, int OtherGroupCount, int OtherCount, int OtherAssignedCount);

/// <summary>A city, department or brand with how many assets are there and how many of them are assigned.</summary>
public sealed record DistributionItem(int Id, string Name, int Count, int AssignedCount);

/// <summary>How many assets of a type there are, largest first.</summary>
public sealed record TypeCount(AssetType AssetType, int Count);

/// <summary>Assignments made and returns taken in a month.</summary>
/// <param name="Month"><c>yyyy-MM</c>, in the reporting time zone.</param>
public sealed record MonthlyMovement(string Month, int AssignedCount, int ReturnedCount);

/// <summary>An asset audit record, with the asset's current code.</summary>
public sealed record RecentActivity(long Id, int AssetId, string? AssetCode, AuditAction Action, string UserName, DateTimeOffset Timestamp);

/// <summary>Reads the dashboard figures from the database.</summary>
public interface IDashboardStore
{
    /// <summary>How many records <see cref="DashboardStatistics.RecentActivity"/> holds.</summary>
    const int RecentActivityCount = 10;

    /// <summary>How many cities, departments or brands a <see cref="Distribution"/> names; the rest are counted together.</summary>
    const int TopGroupCount = 8;

    /// <summary>How many months <see cref="DashboardStatistics.MonthlyMovements"/> covers.</summary>
    const int MovementMonths = 12;

    Task<DashboardStatistics> GetStatisticsAsync(CancellationToken cancellationToken);
}
