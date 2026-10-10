using EnterpriseInventory.Application.Assets;
using EnterpriseInventory.Application.Reports;
using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Infrastructure.Assets;
using EnterpriseInventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseInventory.Infrastructure.Reports;

internal sealed class ReportStore(ApplicationDbContext db) : IReportStore
{
    public async Task<IReadOnlyList<AssetGroupCount>> CountAssetsAsync(
        AssetListCriteria criteria, AssetGrouping grouping, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        // The soft-delete filter stays on: the summary counts the inventory, not the archive.
        var assets = AssetStore.Filter(db.Assets.AsNoTracking(), criteria);
        // Every grouping has the same key shape (one anonymous type), so one query counts them all.
        var groups = grouping switch
        {
            AssetGrouping.Department => assets.GroupBy(a => new
            {
                Id = (int?)a.DepartmentId, Name = (string?)a.Department.Name, ParentId = (int?)null, ParentName = (string?)null,
            }),
            AssetGrouping.Location => assets.GroupBy(a => new
            {
                Id = a.LocationId,
                Name = a.Location != null ? a.Location.Name : null,
                ParentId = a.Location != null ? (int?)a.Location.CityId : null,
                ParentName = a.Location != null ? a.Location.City.Name : null,
            }),
            AssetGrouping.Brand => assets.GroupBy(a => new
            {
                Id = (int?)a.BrandId, Name = (string?)a.Brand.Name, ParentId = (int?)null, ParentName = (string?)null,
            }),
            AssetGrouping.Model => assets.GroupBy(a => new
            {
                Id = (int?)a.ModelId, Name = (string?)a.Model.Name, ParentId = (int?)a.Model.BrandId, ParentName = (string?)a.Model.Brand.Name,
            }),
            AssetGrouping.AssetType => assets.GroupBy(a => new
            {
                Id = (int?)a.AssetType, Name = (string?)null, ParentId = (int?)null, ParentName = (string?)null,
            }),
            AssetGrouping.Status => assets.GroupBy(a => new
            {
                Id = (int?)a.Status, Name = (string?)null, ParentId = (int?)null, ParentName = (string?)null,
            }),
            _ => assets.GroupBy(a => new
            {
                Id = (int?)a.CityId, Name = (string?)a.City.Name, ParentId = (int?)null, ParentName = (string?)null,
            }),
        };

        return await groups
            .Select(g => new AssetGroupCount(
                g.Key.Id,
                g.Key.Name,
                g.Key.ParentId,
                g.Key.ParentName,
                g.Count(),
                g.Count(a => a.Status == AssetStatus.Assigned),
                g.Count(a => a.Status == AssetStatus.Available),
                g.Count(a => a.Status == AssetStatus.Faulty),
                g.Count(a => a.Status == AssetStatus.Retired)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AssignmentReport> ListMovementsAsync(AssignmentReportCriteria criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var (assigned, returned) = await CountMovementsAsync(criteria, cancellationToken).ConfigureAwait(false);
        var items = await Sorted(Movements(criteria))
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new AssignmentReport(items.ConvertAll(Read), criteria.Page, criteria.PageSize, assigned + returned, assigned, returned);
    }

    public async Task<AssignmentReport> ExportMovementsAsync(AssignmentReportCriteria criteria, int maxRows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var (assigned, returned) = await CountMovementsAsync(criteria, cancellationToken).ConfigureAwait(false);
        if (assigned + returned > maxRows)
        {
            return new AssignmentReport([], 1, maxRows, assigned + returned, assigned, returned);
        }

        // One more than allowed: movements made since the count are not cut off silently.
        var items = await Sorted(Movements(criteria)).Take(maxRows + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        return items.Count > maxRows || items.Count != assigned + returned
            ? new AssignmentReport([], 1, maxRows, Math.Max(items.Count, assigned + returned), assigned, returned)
            : new AssignmentReport(items.ConvertAll(Read), 1, maxRows, items.Count, assigned, returned);
    }

    private async Task<(int Assigned, int Returned)> CountMovementsAsync(AssignmentReportCriteria criteria, CancellationToken cancellationToken)
    {
        var assigned = criteria.Includes(AssignmentMovementKind.Assigned)
            ? await Given(criteria).CountAsync(cancellationToken).ConfigureAwait(false)
            : 0;
        var returned = criteria.Includes(AssignmentMovementKind.Returned)
            ? await TakenBack(criteria).CountAsync(cancellationToken).ConfigureAwait(false)
            : 0;
        return (assigned, returned);
    }

    /// <summary>The movements asked for as one query (UNION ALL of assignments given and returned), unsorted.</summary>
    private IQueryable<MovementRow> Movements(AssignmentReportCriteria criteria)
    {
        var given = Given(criteria).Select(x => new MovementRow
        {
            AssignmentId = x.Id,
            Movement = AssignmentMovementKind.Assigned,
            At = x.AssignedAt,
            By = x.AssignedBy,
            AssetId = x.AssetId,
            AssetCode = x.Asset.AssetCode,
            AssetType = x.Asset.AssetType,
            BrandName = x.Asset.Brand.Name,
            ModelName = x.Asset.Model.Name,
            SerialNumber = x.Asset.SerialNumber,
            EmployeeUserName = x.Employee.SamAccountName,
            EmployeeDisplayName = x.Employee.DisplayName,
            AssignmentDescription = x.AssignmentDescription,
            CityName = x.Asset.City.Name,
            DepartmentName = x.Asset.Department.Name,
            AssetArchived = x.Asset.IsDeleted,
        });
        var takenBack = TakenBack(criteria).Select(x => new MovementRow
        {
            AssignmentId = x.Id,
            Movement = AssignmentMovementKind.Returned,
            At = x.ReturnedAt!.Value,
            By = x.ReturnedBy!,
            AssetId = x.AssetId,
            AssetCode = x.Asset.AssetCode,
            AssetType = x.Asset.AssetType,
            BrandName = x.Asset.Brand.Name,
            ModelName = x.Asset.Model.Name,
            SerialNumber = x.Asset.SerialNumber,
            EmployeeUserName = x.Employee.SamAccountName,
            EmployeeDisplayName = x.Employee.DisplayName,
            AssignmentDescription = x.AssignmentDescription,
            CityName = x.Asset.City.Name,
            DepartmentName = x.Asset.Department.Name,
            AssetArchived = x.Asset.IsDeleted,
        });

        return (criteria.Includes(AssignmentMovementKind.Assigned), criteria.Includes(AssignmentMovementKind.Returned)) switch
        {
            (true, false) => given,
            (false, true) => takenBack,
            _ => given.Concat(takenBack),
        };
    }

    /// <summary>Newest first; at the same moment a return before an assignment, then the newer assignment first, so pages never overlap.</summary>
    private static IQueryable<MovementRow> Sorted(IQueryable<MovementRow> movements) =>
        movements.OrderByDescending(m => m.At).ThenByDescending(m => m.Movement).ThenByDescending(m => m.AssignmentId);

    private IQueryable<AssetAssignment> Given(AssignmentReportCriteria criteria)
    {
        var assignments = Matching(criteria);
        if (criteria.Start is { } start)
        {
            assignments = assignments.Where(x => x.AssignedAt >= start);
        }

        if (criteria.End is { } end)
        {
            assignments = assignments.Where(x => x.AssignedAt < end);
        }

        return assignments;
    }

    private IQueryable<AssetAssignment> TakenBack(AssignmentReportCriteria criteria)
    {
        var assignments = Matching(criteria).Where(x => x.ReturnedAt != null);
        if (criteria.Start is { } start)
        {
            assignments = assignments.Where(x => x.ReturnedAt >= start);
        }

        if (criteria.End is { } end)
        {
            assignments = assignments.Where(x => x.ReturnedAt < end);
        }

        return assignments;
    }

    /// <summary>
    /// Assignments of the assets and employees asked for, archived assets included: their movements happened.
    /// Search ignores case and accents, like the inventory list's (see <see cref="AssetStore.SearchCollation"/>).
    /// </summary>
    private IQueryable<AssetAssignment> Matching(AssignmentReportCriteria criteria)
    {
        var assignments = db.AssetAssignments.IncludingArchived().AsNoTracking();
        foreach (var term in criteria.SearchTerms)
        {
            assignments = assignments.Where(x =>
                EF.Functions.Collate(x.Asset.AssetCode, AssetStore.SearchCollation).Contains(term)
                || EF.Functions.Collate(x.Employee.SamAccountName, AssetStore.SearchCollation).Contains(term)
                || EF.Functions.Collate(x.Employee.DisplayName, AssetStore.SearchCollation).Contains(term));
        }

        if (criteria.AssetTypes.Count > 0)
        {
            assignments = assignments.Where(x => criteria.AssetTypes.Contains(x.Asset.AssetType));
        }

        if (criteria.CityId is { } cityId)
        {
            assignments = assignments.Where(x => x.Asset.CityId == cityId);
        }

        if (criteria.DepartmentId is { } departmentId)
        {
            assignments = assignments.Where(x => x.Asset.DepartmentId == departmentId);
        }

        return assignments;
    }

    private static AssignmentMovement Read(MovementRow m) => new(
        m.AssignmentId,
        m.Movement,
        m.At,
        m.By,
        m.AssetId,
        m.AssetCode,
        m.AssetType,
        m.BrandName,
        m.ModelName,
        m.SerialNumber,
        m.EmployeeUserName,
        m.EmployeeDisplayName,
        m.AssignmentDescription,
        m.CityName,
        m.DepartmentName,
        m.AssetArchived);

    /// <summary>A movement as both halves of the UNION read it; the two projections must list the same members in the same order.</summary>
    private sealed class MovementRow
    {
        public int AssignmentId { get; init; }

        public AssignmentMovementKind Movement { get; init; }

        public DateTimeOffset At { get; init; }

        public string By { get; init; } = string.Empty;

        public int AssetId { get; init; }

        public string AssetCode { get; init; } = string.Empty;

        public AssetType AssetType { get; init; }

        public string BrandName { get; init; } = string.Empty;

        public string ModelName { get; init; } = string.Empty;

        public string? SerialNumber { get; init; }

        public string EmployeeUserName { get; init; } = string.Empty;

        public string EmployeeDisplayName { get; init; } = string.Empty;

        public string? AssignmentDescription { get; init; }

        public string CityName { get; init; } = string.Empty;

        public string DepartmentName { get; init; } = string.Empty;

        public bool AssetArchived { get; init; }
    }
}
