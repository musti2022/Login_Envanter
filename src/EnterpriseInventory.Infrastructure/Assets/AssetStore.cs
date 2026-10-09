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
    public async Task<PagedResult<AssetListItem>> ListAsync(AssetListCriteria criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var assets = db.Assets.AsNoTracking().Where(a => !a.IsDeleted);
        var totalCount = await assets.CountAsync(cancellationToken).ConfigureAwait(false);

        // The ID breaks ties, so every row appears on exactly one page.
        var rows = await assets
            .OrderBy(a => a.AssetCode)
            .ThenBy(a => a.Id)
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

    public async Task<AssetDetails?> FindAsync(int id, CancellationToken cancellationToken)
    {
        var row = await db.Assets.AsNoTracking()
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

        var (references, errors) = await LoadReferencesAsync(draft, current: null, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return AssetWriteResult.Invalid(errors);
        }

        var duplicates = await FindDuplicatesAsync(draft, exceptAssetId: 0, cancellationToken).ConfigureAwait(false);
        if (duplicates.Count > 0)
        {
            return AssetWriteResult.Duplicate(duplicates);
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
            return AssetWriteResult.Rule(ex.Code);
        }

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            db.Assets.Add(asset);

            // Saved first so the audit record can name the asset by its ID.
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            Audit(asset, AuditAction.Created, oldValues: null, AssetAuditTrail.Serialize(AssetAuditTrail.Snapshot(asset)));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueKeyViolation())
        {
            // Another request took the code or serial number between the check above and the insert.
            db.ChangeTracker.Clear();
            return AssetWriteResult.Duplicate(DuplicateField(ex));
        }

        LogCreated(asset.Id, asset.AssetCode);
        return AssetWriteResult.Succeeded(await FindAsync(asset.Id, cancellationToken).ConfigureAwait(false));
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

        // The caller edited an older version: refuse before looking at anything else. The same check is repeated
        // by the database on save (the rowversion is part of the UPDATE's WHERE), which catches edits that land
        // between this read and the save.
        if (!asset.RowVersion.AsSpan().SequenceEqual(rowVersion))
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

        foreach (var (action, oldValues, newValues) in changes)
        {
            Audit(asset, action, oldValues, newValues);
        }

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

    public async Task<AssetWriteResult> ArchiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rowVersion);

        var asset = await LoadForChangeAsync(id, cancellationToken).ConfigureAwait(false);
        if (asset is null)
        {
            return AssetWriteResult.NotFound;
        }

        if (!asset.RowVersion.AsSpan().SequenceEqual(rowVersion))
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

        foreach (var (action, oldValues, newValues) in AssetAuditTrail.Changes(before, AssetAuditTrail.Snapshot(asset)))
        {
            Audit(asset, action, oldValues, newValues);
        }

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
        if (!await db.Assets.AnyAsync(a => a.Id == id, cancellationToken).ConfigureAwait(false))
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

    /// <summary>The asset with everything its audit snapshot names, tracked so it can be changed.</summary>
    private Task<Asset?> LoadForChangeAsync(int id, CancellationToken cancellationToken) =>
        db.Assets
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

        var city = await db.Cities.SingleOrDefaultAsync(c => c.Id == draft.CityId, cancellationToken).ConfigureAwait(false);
        if (city is null)
        {
            errors[nameof(AssetDraft.CityId)] = [AssetMessages.CityNotFound];
        }
        else if (current?.CityId != draft.CityId && !city.IsActive)
        {
            errors[nameof(AssetDraft.CityId)] = [AssetMessages.CityInactive];
        }

        var department = await db.Departments.SingleOrDefaultAsync(d => d.Id == draft.DepartmentId, cancellationToken).ConfigureAwait(false);
        if (department is null)
        {
            errors[nameof(AssetDraft.DepartmentId)] = [AssetMessages.DepartmentNotFound];
        }
        else if (current?.DepartmentId != draft.DepartmentId && !department.IsActive)
        {
            errors[nameof(AssetDraft.DepartmentId)] = [AssetMessages.DepartmentInactive];
        }

        Location? location = null;
        if (draft.LocationId is { } locationId)
        {
            location = await db.Locations.SingleOrDefaultAsync(l => l.Id == locationId, cancellationToken).ConfigureAwait(false);
            if (location is null)
            {
                errors[nameof(AssetDraft.LocationId)] = [AssetMessages.LocationNotFound];
            }
            else if (location.CityId != draft.CityId)
            {
                errors[nameof(AssetDraft.LocationId)] = [AssetMessages.LocationInAnotherCity];
            }
            else if (current?.LocationId != locationId && !location.IsActive)
            {
                errors[nameof(AssetDraft.LocationId)] = [AssetMessages.LocationInactive];
            }
        }

        return (new References(model!, city!, department!, location), errors);
    }

    /// <summary>Asset codes and serial numbers are unique across all assets, archived ones included.</summary>
    private async Task<Dictionary<string, string[]>> FindDuplicatesAsync(AssetDraft draft, int exceptAssetId, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        // The columns' Turkish_CI_AS collation makes these comparisons case-insensitive, like the unique indexes.
        if (await db.Assets.AnyAsync(a => a.AssetCode == draft.AssetCode && a.Id != exceptAssetId, cancellationToken).ConfigureAwait(false))
        {
            errors[nameof(AssetDraft.AssetCode)] = [AssetMessages.AssetCodeTaken];
        }

        if (draft.SerialNumber is { } serial
            && await db.Assets.AnyAsync(a => a.SerialNumber == serial && a.Id != exceptAssetId, cancellationToken).ConfigureAwait(false))
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

    private void Audit(Asset asset, AuditAction action, string? oldValues, string? newValues) =>
        db.AuditLogs.Add(AuditLog.Create(
            AssetAuditTrail.EntityName,
            AssetAuditTrail.EntityId(asset),
            action,
            oldValues,
            newValues,
            currentUser.UserName ?? throw new InvalidOperationException("Assets can only be changed by a signed-in user."),
            timeProvider.GetUtcNow(),
            requestContext.CorrelationId));

    [LoggerMessage(Level = LogLevel.Information, Message = "Asset {AssetId} ({AssetCode}) created")]
    private partial void LogCreated(int assetId, string assetCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Asset {AssetId} ({AssetCode}) archived")]
    private partial void LogArchived(int assetId, string assetCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Asset {AssetId} updated ({AuditActions})")]
    private partial void LogUpdated(int assetId, string auditActions);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Asset {AssetId} not saved: it was changed by someone else since the caller read it")]
    private partial void LogConflict(int assetId);

    private sealed record References(AssetModel Model, City City, Department Department, Location? Location);
}
