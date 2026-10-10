using System.Globalization;
using EnterpriseInventory.Application.Assets;
using EnterpriseInventory.Application.Auditing;
using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Domain.Auditing;
using EnterpriseInventory.Domain.Catalog;
using EnterpriseInventory.Domain.Organization;
using EnterpriseInventory.Domain.Users;
using EnterpriseInventory.Infrastructure.Assets;
using EnterpriseInventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseInventory.Infrastructure.Auditing;

internal sealed class AuditLogStore(ApplicationDbContext db) : IAuditLogStore
{
    public async Task<PagedResult<AuditLogEntry>> ListAsync(AuditLogCriteria criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        // The assets a code matches, found first when there are few: their records are then read through the
        // (EntityName, EntityId) index instead of comparing every record with the matching codes (docs/performance.md).
        List<string>? assetIds = null;
        if (criteria.AssetCode is { } assetCode)
        {
            var ids = await AssetIdsMatching(assetCode).Take(AssetStore.MaxListedMatches + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
            assetIds = ids.Count <= AssetStore.MaxListedMatches ? ids : null;
        }

        var records = Filter(db.AuditLogs.AsNoTracking(), criteria, assetIds);
        var totalCount = await records.CountAsync(cancellationToken).ConfigureAwait(false);

        // Newest first. The ID is the order records were written in, even when two share a timestamp.
        var rows = await records
            .OrderByDescending(l => l.Id)
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .Select(l => new Row(l.Id, l.EntityName, l.EntityId, l.Action, l.UserName, l.Timestamp, l.CorrelationId, l.OldValues, l.NewValues))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var labels = await LabelsAsync(rows, cancellationToken).ConfigureAwait(false);
        return new PagedResult<AuditLogEntry>(rows.ConvertAll(row => row.ToEntry(labels)), criteria.Page, criteria.PageSize, totalCount);
    }

    /// <summary>
    /// The IDs, as audit records store them, of the assets whose code contains <paramref name="assetCode"/>. Archived
    /// assets too: their records are history like any other. Contains escapes % and _.
    /// </summary>
    private IQueryable<string> AssetIdsMatching(string assetCode) =>
#pragma warning disable CA1305 // ToString runs in SQL Server (CONVERT), where no culture applies.
        db.Assets.IncludingArchived()
            .Where(a => EF.Functions.Collate(a.AssetCode, AssetStore.SearchCollation).Contains(assetCode))
            .Select(a => a.Id.ToString());
#pragma warning restore CA1305

    public async Task<AuditLogEntry?> FindAsync(long id, CancellationToken cancellationToken)
    {
        var row = await db.AuditLogs.AsNoTracking()
            .Where(l => l.Id == id)
            .Select(l => new Row(l.Id, l.EntityName, l.EntityId, l.Action, l.UserName, l.Timestamp, l.CorrelationId, l.OldValues, l.NewValues))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return row?.ToEntry(await LabelsAsync([row], cancellationToken).ConfigureAwait(false));
    }

    private IQueryable<AuditLog> Filter(IQueryable<AuditLog> records, AuditLogCriteria criteria, List<string>? listedAssetIds)
    {
        if (criteria.EntityName is { } entityName)
        {
            records = records.Where(l => l.EntityName == entityName);
        }

        if (criteria.EntityId is { } entityId)
        {
            records = records.Where(l => l.EntityId == entityId);
        }

        if (listedAssetIds is not null)
        {
            records = records.Where(l => l.EntityName == AssetAuditTrail.EntityName && listedAssetIds.Contains(l.EntityId));
        }
        else if (criteria.AssetCode is { } assetCode)
        {
            var assetIds = AssetIdsMatching(assetCode);
            records = records.Where(l => l.EntityName == AssetAuditTrail.EntityName && assetIds.Contains(l.EntityId));
        }

        if (criteria.Actions.Count > 0)
        {
            records = records.Where(l => criteria.Actions.Contains(l.Action));
        }

        if (criteria.UserName is { } userName)
        {
            records = records.Where(l => EF.Functions.Collate(l.UserName, AssetStore.SearchCollation).Contains(userName));
        }

        if (criteria.From is { } from)
        {
            records = records.Where(l => l.Timestamp >= from);
        }

        if (criteria.To is { } to)
        {
            records = records.Where(l => l.Timestamp < to);
        }

        if (criteria.CorrelationId is { } correlationId)
        {
            records = records.Where(l => l.CorrelationId == correlationId);
        }

        return records;
    }

    /// <summary>
    /// The current name of each record the rows are about, by entity and ID: one query per kind of record on the
    /// page. Archived assets and inactive lookups are named too; a record that is gone has no name.
    /// </summary>
    private async Task<Dictionary<(string EntityName, string EntityId), string>> LabelsAsync(IReadOnlyCollection<Row> rows, CancellationToken cancellationToken)
    {
        var labels = new Dictionary<(string, string), string>();
        foreach (var group in rows.GroupBy(r => r.EntityName))
        {
            var ids = group
                .Select(r => int.TryParse(r.EntityId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0)
                .Where(id => id > 0)
                .Distinct()
                .ToList();
            if (ids.Count == 0)
            {
                continue;
            }

            IQueryable<IdName>? names = group.Key switch
            {
                nameof(Asset) => db.Assets.IncludingArchived().Where(a => ids.Contains(a.Id)).Select(a => new IdName(a.Id, a.AssetCode)),
                nameof(Brand) => db.Brands.Where(b => ids.Contains(b.Id)).Select(b => new IdName(b.Id, b.Name)),
                nameof(AssetModel) => db.AssetModels.Where(m => ids.Contains(m.Id)).Select(m => new IdName(m.Id, m.Name)),
                nameof(City) => db.Cities.Where(c => ids.Contains(c.Id)).Select(c => new IdName(c.Id, c.Name)),
                nameof(Department) => db.Departments.Where(d => ids.Contains(d.Id)).Select(d => new IdName(d.Id, d.Name)),
                nameof(Location) => db.Locations.Where(l => ids.Contains(l.Id)).Select(l => new IdName(l.Id, l.Name)),
                nameof(AdminUser) => db.AdminUsers.Where(u => ids.Contains(u.Id)).Select(u => new IdName(u.Id, u.SamAccountName)),
                _ => null,
            };
            if (names is null)
            {
                continue;
            }

            foreach (var (id, name) in await names.ToListAsync(cancellationToken).ConfigureAwait(false))
            {
                labels[(group.Key, id.ToString(CultureInfo.InvariantCulture))] = name;
            }
        }

        return labels;
    }

    private sealed record IdName(int Id, string Name);

    private sealed record Row(
        long Id,
        string EntityName,
        string EntityId,
        AuditAction Action,
        string UserName,
        DateTimeOffset Timestamp,
        string CorrelationId,
        string? OldValues,
        string? NewValues)
    {
        public AuditLogEntry ToEntry(Dictionary<(string EntityName, string EntityId), string> labels) => new(
            Id,
            EntityName,
            EntityId,
            labels.GetValueOrDefault((EntityName, EntityId)),
            Action,
            UserName,
            Timestamp,
            CorrelationId,
            AssetAuditTrail.Parse(OldValues),
            AssetAuditTrail.Parse(NewValues));
    }
}
