using System.Globalization;
using EnterpriseInventory.Application.Abstractions;
using EnterpriseInventory.Application.Assets;
using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Domain.Auditing;
using EnterpriseInventory.Domain.Catalog;
using EnterpriseInventory.Domain.Common;
using EnterpriseInventory.Domain.Organization;
using EnterpriseInventory.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnterpriseInventory.Infrastructure.Assets;

internal sealed partial class AssetStore(
    ApplicationDbContext db,
    ICurrentUser currentUser,
    IRequestContext requestContext,
    TimeProvider timeProvider,
    ILogger<AssetStore> logger) : IAssetStore
{
    /// <summary>
    /// Search ignores case and accents, and treats i, ı, İ and I as one letter: under the database's
    /// Turkish_CI_AS, "pc-ist" would not find "PC-IST-01" (Turkish i is not I) and "canta" would not find "çanta".
    /// Uniqueness and sorting keep the Turkish rules.
    /// </summary>
    internal const string SearchCollation = "Latin1_General_100_CI_AI";

    public async Task<PagedResult<AssetListItem>> ListAsync(AssetListCriteria criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        // Normal lists rely on the soft-delete filter; only the archive list turns it off, to show archived assets.
        var source = criteria.Archived ? db.Assets.IncludingArchived().Where(a => a.IsDeleted) : db.Assets;
        var assets = Filter(source.AsNoTracking(), criteria);
        var totalCount = await assets.CountAsync(cancellationToken).ConfigureAwait(false);

        var rows = await Sort(assets, criteria)
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .Select(a => new
            {
                a.Id,
                a.AssetCode,
                a.ComputerName,
                BrandName = a.Brand.Name,
                ModelName = a.Model.Name,
                a.SerialNumber,
                a.AssetType,
                a.Status,
                CityName = a.City.Name,
                DepartmentName = a.Department.Name,
                LocationName = a.Location != null ? a.Location.Name : null,
                Holder = a.Assignments
                    .Where(x => x.ReturnedAt == null)
                    .Select(x => new { x.Employee.SamAccountName, x.Employee.DisplayName, x.AssignmentDescription })
                    .FirstOrDefault(),
                a.IsDeleted,
                a.CreatedAt,
                a.UpdatedAt,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = rows.ConvertAll(r => new AssetListItem(
            r.Id,
            r.AssetCode,
            r.ComputerName,
            r.BrandName,
            r.ModelName,
            r.SerialNumber,
            r.AssetType,
            r.Status,
            r.CityName,
            r.DepartmentName,
            r.LocationName,
            r.Holder?.SamAccountName,
            r.Holder?.DisplayName,
            r.Holder?.AssignmentDescription,
            r.IsDeleted,
            r.CreatedAt,
            r.UpdatedAt));
        return new PagedResult<AssetListItem>(items, criteria.Page, criteria.PageSize, totalCount);
    }

    private static IQueryable<Asset> Filter(IQueryable<Asset> assets, AssetListCriteria criteria)
    {
        // Contains becomes LIKE with its wildcards escaped, so % and _ are plain characters.
        foreach (var term in criteria.SearchTerms)
        {
            assets = assets.Where(a =>
                EF.Functions.Collate(a.AssetCode, SearchCollation).Contains(term)
                || EF.Functions.Collate(a.ComputerName!, SearchCollation).Contains(term)
                || EF.Functions.Collate(a.SerialNumber!, SearchCollation).Contains(term)
                || EF.Functions.Collate(a.Brand.Name, SearchCollation).Contains(term)
                || EF.Functions.Collate(a.Model.Name, SearchCollation).Contains(term)
                || EF.Functions.Collate(a.City.Name, SearchCollation).Contains(term)
                || EF.Functions.Collate(a.Department.Name, SearchCollation).Contains(term)
                || EF.Functions.Collate(a.Location!.Name, SearchCollation).Contains(term)
                || a.Assignments.Any(x => x.ReturnedAt == null
                    && (EF.Functions.Collate(x.Employee.SamAccountName, SearchCollation).Contains(term)
                        || EF.Functions.Collate(x.Employee.DisplayName, SearchCollation).Contains(term)
                        || EF.Functions.Collate(x.AssignmentDescription!, SearchCollation).Contains(term))));
        }

        if (criteria.Statuses.Count > 0)
        {
            assets = assets.Where(a => criteria.Statuses.Contains(a.Status));
        }

        if (criteria.AssetTypes.Count > 0)
        {
            assets = assets.Where(a => criteria.AssetTypes.Contains(a.AssetType));
        }

        if (criteria.BrandId is { } brandId)
        {
            assets = assets.Where(a => a.BrandId == brandId);
        }

        if (criteria.ModelId is { } modelId)
        {
            assets = assets.Where(a => a.ModelId == modelId);
        }

        if (criteria.CityId is { } cityId)
        {
            assets = assets.Where(a => a.CityId == cityId);
        }

        if (criteria.DepartmentId is { } departmentId)
        {
            assets = assets.Where(a => a.DepartmentId == departmentId);
        }

        if (criteria.LocationId is { } locationId)
        {
            assets = assets.Where(a => a.LocationId == locationId);
        }

        return assets;
    }

    /// <summary>The chosen column first; the asset code (unique) and ID break ties, so pages never overlap.</summary>
    private static IQueryable<Asset> Sort(IQueryable<Asset> assets, AssetListCriteria criteria)
    {
        var descending = criteria.Descending;
        var sorted = criteria.SortBy switch
        {
            AssetSortField.ComputerName => By(a => a.ComputerName),
            AssetSortField.SerialNumber => By(a => a.SerialNumber),
            AssetSortField.BrandName => By(a => a.Brand.Name),
            AssetSortField.ModelName => By(a => a.Model.Name),
            AssetSortField.AssetType => By(a => a.AssetType),
            AssetSortField.Status => By(a => a.Status),
            AssetSortField.CityName => By(a => a.City.Name),
            AssetSortField.DepartmentName => By(a => a.Department.Name),
            AssetSortField.LocationName => By(a => a.Location != null ? a.Location.Name : null),
            AssetSortField.AssignedUserName => By(a => a.Assignments.Where(x => x.ReturnedAt == null).Select(x => x.Employee.SamAccountName).FirstOrDefault()),
            AssetSortField.AssignedDisplayName => By(a => a.Assignments.Where(x => x.ReturnedAt == null).Select(x => x.Employee.DisplayName).FirstOrDefault()),
            AssetSortField.CreatedAt => By(a => a.CreatedAt),
            AssetSortField.UpdatedAt => By(a => a.UpdatedAt),
            _ => By(a => a.AssetCode),
        };
        return descending
            ? sorted.ThenByDescending(a => a.AssetCode).ThenByDescending(a => a.Id)
            : sorted.ThenBy(a => a.AssetCode).ThenBy(a => a.Id);

        IOrderedQueryable<Asset> By<TKey>(System.Linq.Expressions.Expression<Func<Asset, TKey>> key) =>
            descending ? assets.OrderByDescending(key) : assets.OrderBy(key);
    }

    /// <summary>Archived assets too: their detail page shows them as archived.</summary>
    public async Task<AssetDetails?> FindAsync(int id, CancellationToken cancellationToken)
    {
        var row = await db.Assets.IncludingArchived().AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new
            {
                a.Id,
                a.AssetCode,
                a.ComputerName,
                a.AssetType,
                a.Status,
                a.SerialNumber,
                a.Description,
                Brand = new NamedReference(a.Brand.Id, a.Brand.Name),
                Model = new NamedReference(a.Model.Id, a.Model.Name),
                City = new NamedReference(a.City.Id, a.City.Name),
                Department = new NamedReference(a.Department.Id, a.Department.Name),
                Location = a.Location != null ? new NamedReference(a.Location.Id, a.Location.Name) : null,
                ActiveAssignment = a.Assignments
                    .Where(x => x.ReturnedAt == null)
                    .Select(x => new ActiveAssignmentInfo(
                        x.Id, x.EmployeeId, x.Employee.SamAccountName, x.Employee.DisplayName, x.AssignmentDescription, x.AssignedAt, x.AssignedBy))
                    .FirstOrDefault(),
                a.IsDeleted,
                a.CreatedAt,
                a.CreatedBy,
                a.UpdatedAt,
                a.UpdatedBy,
                a.RowVersion,
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null
            ? null
            : new AssetDetails(
                row.Id,
                row.AssetCode,
                row.ComputerName,
                row.AssetType,
                row.Status,
                row.SerialNumber,
                row.Description,
                row.Brand,
                row.Model,
                row.City,
                row.Department,
                row.Location,
                row.ActiveAssignment,
                row.IsDeleted,
                row.CreatedAt,
                row.CreatedBy,
                row.UpdatedAt,
                row.UpdatedBy,
                Convert.ToBase64String(row.RowVersion));
    }

    public async Task<AssetWriteResult> CreateAsync(AssetDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        (AssetWriteResult Result, int Id) created;
        try
        {
            // Checks, insert and audit record are one unit, so a retried attempt starts again from the checks.
            created = await db.InTransactionAsync(
                async (transaction, token) =>
                {
                    var (references, errors) = await LoadReferencesAsync(draft, current: null, token).ConfigureAwait(false);
                    if (errors.Count > 0)
                    {
                        return (AssetWriteResult.Invalid(errors), 0);
                    }

                    var duplicates = await FindDuplicatesAsync(draft, exceptAssetId: 0, token).ConfigureAwait(false);
                    if (duplicates.Count > 0)
                    {
                        return (AssetWriteResult.Duplicate(duplicates), 0);
                    }

                    Asset asset;
                    try
                    {
                        asset = Asset.Create(
                            draft.AssetCode,
                            draft.AssetType,
                            references.Model,
                            references.City,
                            references.Department,
                            references.Location,
                            draft.ComputerName,
                            draft.SerialNumber,
                            draft.Description);
                        if (draft.Status is { } status && status != asset.Status)
                        {
                            asset.ChangeStatus(status);
                        }
                    }
                    catch (DomainException ex)
                    {
                        return (AssetWriteResult.Rule(ex.Code), 0);
                    }

                    db.Assets.Add(asset);

                    // Saved first so the audit record can name the asset by its ID.
                    await db.SaveChangesAsync(token).ConfigureAwait(false);
                    Audit(asset, AuditAction.Created, oldValues: null, AssetAuditTrail.Serialize(AssetAuditTrail.Snapshot(asset)), timeProvider.GetUtcNow());
                    await db.SaveChangesAsync(token).ConfigureAwait(false);
                    await transaction.CommitAsync(token).ConfigureAwait(false);
                    LogCreated(asset.Id, asset.AssetCode);
                    return (AssetWriteResult.Succeeded(null), asset.Id);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueKeyViolation())
        {
            // Another request took the code or serial number between the check above and the insert.
            db.ChangeTracker.Clear();
            return AssetWriteResult.Duplicate(DuplicateField(ex));
        }

        return created.Result.Outcome == AssetWriteOutcome.Succeeded
            ? AssetWriteResult.Succeeded(await FindAsync(created.Id, cancellationToken).ConfigureAwait(false))
            : created.Result;
    }

    public async Task<AssetWriteResult> UpdateAsync(int id, AssetDraft draft, byte[] rowVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(rowVersion);

        var asset = await LoadForChangeAsync(id, cancellationToken).ConfigureAwait(false);
        if (asset is null)
        {
            return AssetWriteResult.NotFound;
        }

        if (!db.MatchesClientVersion(asset, rowVersion))
        {
            LogConflict(id);
            return AssetWriteResult.Conflict;
        }

        if (asset.IsDeleted)
        {
            return AssetWriteResult.Rule(DomainErrors.Asset.Archived);
        }

        var (references, errors) = await LoadReferencesAsync(draft, asset, cancellationToken).ConfigureAwait(false);
        if (draft.Status == AssetStatus.Assigned && asset.Status != AssetStatus.Assigned)
        {
            errors[nameof(AssetDraft.Status)] = [AssetMessages.StatusAssignedOnlyByAssignment];
        }

        if (errors.Count > 0)
        {
            return AssetWriteResult.Invalid(errors);
        }

        var duplicates = await FindDuplicatesAsync(draft, exceptAssetId: id, cancellationToken).ConfigureAwait(false);
        if (duplicates.Count > 0)
        {
            return AssetWriteResult.Duplicate(duplicates);
        }

        var before = AssetAuditTrail.Snapshot(asset);
        try
        {
            asset.UpdateDetails(draft.AssetCode, draft.AssetType, draft.ComputerName, draft.SerialNumber, draft.Description);
            asset.ChangeModel(references.Model);
            asset.ChangeLocation(references.City, references.Department, references.Location);
            if (draft.Status is { } status && status != asset.Status)
            {
                asset.ChangeStatus(status);
            }
        }
        catch (DomainException ex)
        {
            db.ChangeTracker.Clear();
            return AssetWriteResult.Rule(ex.Code);
        }

        var changes = AssetAuditTrail.Changes(before, AssetAuditTrail.Snapshot(asset)).ToList();
        if (changes.Count == 0)
        {
            return AssetWriteResult.Succeeded(await FindAsync(id, cancellationToken).ConfigureAwait(false));
        }

        Audit(asset, changes);

        try
        {
            // One SaveChanges: the asset and its audit records are committed together or not at all.
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            LogConflict(id);
            return AssetWriteResult.Conflict;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueKeyViolation())
        {
            db.ChangeTracker.Clear();
            return AssetWriteResult.Duplicate(DuplicateField(ex));
        }

        var actions = string.Join(", ", changes.Select(c => c.Action));
        LogUpdated(id, actions);
        return AssetWriteResult.Succeeded(await FindAsync(id, cancellationToken).ConfigureAwait(false));
    }

    public async Task<AssetWriteResult> ChangeLocationAsync(int id, AssetPlacement placement, byte[] rowVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(rowVersion);

        var asset = await LoadForChangeAsync(id, cancellationToken).ConfigureAwait(false);
        if (asset is null)
        {
            return AssetWriteResult.NotFound;
        }

        if (!db.MatchesClientVersion(asset, rowVersion))
        {
            LogConflict(id);
            return AssetWriteResult.Conflict;
        }

        if (asset.IsDeleted)
        {
            return AssetWriteResult.Rule(DomainErrors.Asset.Archived);
        }

        var errors = new Dictionary<string, string[]>();
        var place = await LoadPlacementAsync(placement.CityId, placement.DepartmentId, placement.LocationId, asset, errors, cancellationToken)
            .ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return AssetWriteResult.Invalid(errors);
        }

        var before = AssetAuditTrail.Snapshot(asset);
        try
        {
            asset.ChangeLocation(place.City, place.Department, place.Location);
        }
        catch (DomainException ex)
        {
            db.ChangeTracker.Clear();
            return AssetWriteResult.Rule(ex.Code);
        }

        var changes = AssetAuditTrail.Changes(before, AssetAuditTrail.Snapshot(asset)).ToList();
        if (changes.Count == 0)
        {
            return AssetWriteResult.Succeeded(await FindAsync(id, cancellationToken).ConfigureAwait(false));
        }

        Audit(asset, changes);

        try
        {
            // One SaveChanges: the move and its audit record are committed together or not at all.
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            LogConflict(id);
            return AssetWriteResult.Conflict;
        }

        LogMoved(id);
        return AssetWriteResult.Succeeded(await FindAsync(id, cancellationToken).ConfigureAwait(false));
    }

    public async Task<AssetWriteResult> ArchiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rowVersion);

        var asset = await LoadForChangeAsync(id, cancellationToken).ConfigureAwait(false);
        if (asset is null)
        {
            return AssetWriteResult.NotFound;
        }

        if (!db.MatchesClientVersion(asset, rowVersion))
        {
            LogConflict(id);
            return AssetWriteResult.Conflict;
        }

        var before = AssetAuditTrail.Snapshot(asset);
        try
        {
            asset.Archive();
        }
        catch (DomainException ex)
        {
            return AssetWriteResult.Rule(ex.Code);
        }

        Audit(asset, AssetAuditTrail.Changes(before, AssetAuditTrail.Snapshot(asset)));

        try
        {
            // One SaveChanges: the archive flag and its audit record are committed together or not at all.
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            LogConflict(id);
            return AssetWriteResult.Conflict;
        }

        LogArchived(id, asset.AssetCode);
        return AssetWriteResult.Succeeded(await FindAsync(id, cancellationToken).ConfigureAwait(false));
    }

    public async Task<PagedResult<AssetHistoryEntry>?> HistoryAsync(int id, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!await db.Assets.IncludingArchived().AnyAsync(a => a.Id == id, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var entityId = id.ToString(CultureInfo.InvariantCulture);
        var records = db.AuditLogs.AsNoTracking().Where(a => a.EntityName == AssetAuditTrail.EntityName && a.EntityId == entityId);
        var totalCount = await records.CountAsync(cancellationToken).ConfigureAwait(false);

        // Newest first. The ID is the order records were written in, even when two share a timestamp.
        var rows = await records
            .OrderByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new { a.Id, a.Action, a.UserName, a.Timestamp, a.CorrelationId, a.OldValues, a.NewValues })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = rows.ConvertAll(r => new AssetHistoryEntry(
            r.Id, r.Action, r.UserName, r.Timestamp, r.CorrelationId, AssetAuditTrail.Parse(r.OldValues), AssetAuditTrail.Parse(r.NewValues)));
        return new PagedResult<AssetHistoryEntry>(items, page, pageSize, totalCount);
    }

    /// <summary>
    /// The asset with everything its audit snapshot names, tracked so it can be changed. Archived ones too, so a
    /// change to an archived asset is refused as such instead of as "not found".
    /// </summary>
    private Task<Asset?> LoadForChangeAsync(int id, CancellationToken cancellationToken) =>
        db.Assets
            .IncludingArchived()
            .Include(a => a.Brand)
            .Include(a => a.Model)
            .Include(a => a.City)
            .Include(a => a.Department)
            .Include(a => a.Location)
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    /// <summary>
    /// Loads the chosen lookups. A missing one is an error; an inactive one is an error only when it is newly
    /// chosen, because an asset may keep a value that has since been deactivated (the domain's rule).
    /// </summary>
    private async Task<(References References, Dictionary<string, string[]> Errors)> LoadReferencesAsync(
        AssetDraft draft, Asset? current, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        var model = await db.AssetModels.Include(m => m.Brand)
            .SingleOrDefaultAsync(m => m.Id == draft.ModelId, cancellationToken).ConfigureAwait(false);
        var newModel = current?.ModelId != draft.ModelId;
        if (model is null)
        {
            errors[nameof(AssetDraft.ModelId)] = [AssetMessages.ModelNotFound];
        }
        else if (newModel && !model.IsActive)
        {
            errors[nameof(AssetDraft.ModelId)] = [AssetMessages.ModelInactive];
        }
        else if (newModel && !model.Brand.IsActive)
        {
            errors[nameof(AssetDraft.ModelId)] = [AssetMessages.BrandInactive];
        }

        var place = await LoadPlacementAsync(draft.CityId, draft.DepartmentId, draft.LocationId, current, errors, cancellationToken).ConfigureAwait(false);
        return (new References(model!, place.City, place.Department, place.Location), errors);
    }

    /// <summary>
    /// Loads where an asset is to be: the city, the department and the optional location, which must be in that
    /// city. Problems are added to <paramref name="errors"/> under the request's field names; then the returned
    /// values are not to be used. A value the asset already has may be inactive; a newly chosen one may not.
    /// </summary>
    private async Task<Placement> LoadPlacementAsync(
        int cityId, int departmentId, int? locationId, Asset? current, Dictionary<string, string[]> errors, CancellationToken cancellationToken)
    {
        var city = await db.Cities.SingleOrDefaultAsync(c => c.Id == cityId, cancellationToken).ConfigureAwait(false);
        if (city is null)
        {
            errors[nameof(AssetPlacement.CityId)] = [AssetMessages.CityNotFound];
        }
        else if (current?.CityId != cityId && !city.IsActive)
        {
            errors[nameof(AssetPlacement.CityId)] = [AssetMessages.CityInactive];
        }

        var department = await db.Departments.SingleOrDefaultAsync(d => d.Id == departmentId, cancellationToken).ConfigureAwait(false);
        if (department is null)
        {
            errors[nameof(AssetPlacement.DepartmentId)] = [AssetMessages.DepartmentNotFound];
        }
        else if (current?.DepartmentId != departmentId && !department.IsActive)
        {
            errors[nameof(AssetPlacement.DepartmentId)] = [AssetMessages.DepartmentInactive];
        }

        Location? location = null;
        if (locationId is { } id)
        {
            location = await db.Locations.SingleOrDefaultAsync(l => l.Id == id, cancellationToken).ConfigureAwait(false);
            if (location is null)
            {
                errors[nameof(AssetPlacement.LocationId)] = [AssetMessages.LocationNotFound];
            }
            else if (location.CityId != cityId)
            {
                errors[nameof(AssetPlacement.LocationId)] = [AssetMessages.LocationInAnotherCity];
            }
            else if (current?.LocationId != id && !location.IsActive)
            {
                errors[nameof(AssetPlacement.LocationId)] = [AssetMessages.LocationInactive];
            }
        }

        return new Placement(city!, department!, location);
    }

    /// <summary>Asset codes and serial numbers are unique across all assets, archived ones included.</summary>
    private async Task<Dictionary<string, string[]>> FindDuplicatesAsync(AssetDraft draft, int exceptAssetId, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        // The columns' Turkish_CI_AS collation makes these comparisons case-insensitive, like the unique indexes.
        var assets = db.Assets.IncludingArchived();
        if (await assets.AnyAsync(a => a.AssetCode == draft.AssetCode && a.Id != exceptAssetId, cancellationToken).ConfigureAwait(false))
        {
            errors[nameof(AssetDraft.AssetCode)] = [AssetMessages.AssetCodeTaken];
        }

        if (draft.SerialNumber is { } serial
            && await assets.AnyAsync(a => a.SerialNumber == serial && a.Id != exceptAssetId, cancellationToken).ConfigureAwait(false))
        {
            errors[nameof(AssetDraft.SerialNumber)] = [AssetMessages.SerialNumberTaken];
        }

        return errors;
    }

    private static Dictionary<string, string[]> DuplicateField(DbUpdateException exception)
    {
        var message = (exception.InnerException as SqlException)?.Message ?? string.Empty;
        return message.Contains("IX_Assets_SerialNumber", StringComparison.Ordinal)
            ? new() { [nameof(AssetDraft.SerialNumber)] = [AssetMessages.SerialNumberTaken] }
            : message.Contains("IX_Assets_AssetCode", StringComparison.Ordinal)
                ? new() { [nameof(AssetDraft.AssetCode)] = [AssetMessages.AssetCodeTaken] }
                : throw new InvalidOperationException("An unexpected unique index refused the asset.", exception);
    }

    /// <summary>The audit records of one change, one per kind of change: same user, moment and correlation ID.</summary>
    private void Audit(Asset asset, IEnumerable<(AuditAction Action, string OldValues, string NewValues)> changes)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var (action, oldValues, newValues) in changes)
        {
            Audit(asset, action, oldValues, newValues, now);
        }
    }

    private void Audit(Asset asset, AuditAction action, string? oldValues, string? newValues, DateTimeOffset now) =>
        db.AuditLogs.Add(AuditLog.Create(
            AssetAuditTrail.EntityName,
            AssetAuditTrail.EntityId(asset),
            action,
            oldValues,
            newValues,
            currentUser.UserName ?? throw new InvalidOperationException("Assets can only be changed by a signed-in user."),
            now,
            requestContext.CorrelationId));

    [LoggerMessage(Level = LogLevel.Information, Message = "Asset {AssetId} ({AssetCode}) created")]
    private partial void LogCreated(int assetId, string assetCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Asset {AssetId} ({AssetCode}) archived")]
    private partial void LogArchived(int assetId, string assetCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Asset {AssetId} moved")]
    private partial void LogMoved(int assetId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Asset {AssetId} updated ({AuditActions})")]
    private partial void LogUpdated(int assetId, string auditActions);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Asset {AssetId} not saved: it was changed by someone else since the caller read it")]
    private partial void LogConflict(int assetId);

    private sealed record References(AssetModel Model, City City, Department Department, Location? Location);

    private sealed record Placement(City City, Department Department, Location? Location);
}
