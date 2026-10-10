using System.Globalization;
using EnterpriseInventory.Application.Abstractions;
using EnterpriseInventory.Application.Lookups;
using EnterpriseInventory.Domain.Auditing;
using EnterpriseInventory.Domain.Catalog;
using EnterpriseInventory.Domain.Common;
using EnterpriseInventory.Domain.Organization;
using EnterpriseInventory.Infrastructure.Assets;
using EnterpriseInventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnterpriseInventory.Infrastructure.Lookups;

internal sealed partial class LookupStore(
    ApplicationDbContext db,
    ICurrentUser currentUser,
    IRequestContext requestContext,
    TimeProvider timeProvider,
    ILogger<LookupStore> logger) : ILookupStore
{
    public Task<IReadOnlyList<LookupItem>> ListAsync(LookupKind kind, CancellationToken cancellationToken) => kind switch
    {
        LookupKind.Brand => ListAsync(db.Brands, cancellationToken),
        LookupKind.City => ListAsync(db.Cities, cancellationToken),
        _ => ListAsync(db.Departments, cancellationToken),
    };

    public async Task<IReadOnlyList<ModelItem>> ListModelsAsync(int? brandId, CancellationToken cancellationToken)
    {
        var models = db.AssetModels.AsNoTracking();
        if (brandId is { } id)
        {
            models = models.Where(m => m.BrandId == id);
        }

        // The database's Turkish collation sorts the names, so "Çanta" comes after "Cep".
        return await models
            .OrderBy(m => m.Brand.Name)
            .ThenBy(m => m.Name)
            .ThenBy(m => m.Id)
            .Select(m => new ModelItem(m.Id, m.Name, m.IsActive, m.BrandId, m.Brand.Name, Convert.ToBase64String(m.RowVersion)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LocationItem>> ListLocationsAsync(int? cityId, CancellationToken cancellationToken)
    {
        var locations = db.Locations.AsNoTracking();
        if (cityId is { } id)
        {
            locations = locations.Where(l => l.CityId == id);
        }

        return await locations
            .OrderBy(l => l.City.Name)
            .ThenBy(l => l.Name)
            .ThenBy(l => l.Id)
            .Select(l => new LocationItem(l.Id, l.Name, l.IsActive, l.CityId, l.City.Name, Convert.ToBase64String(l.RowVersion)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LookupWriteResult<LookupItem>> CreateAsync(LookupKind kind, string name, CancellationToken cancellationToken)
    {
        // The name columns' Turkish_CI_AS collation makes these checks case-insensitive, like the unique indexes.
        var (taken, entity) = kind switch
        {
            LookupKind.Brand => (await db.Brands.AnyAsync(b => b.Name == name, cancellationToken).ConfigureAwait(false), (ReferenceDataEntity)Brand.Create(name)),
            LookupKind.City => (await db.Cities.AnyAsync(c => c.Name == name, cancellationToken).ConfigureAwait(false), City.Create(name)),
            _ => (await db.Departments.AnyAsync(d => d.Name == name, cancellationToken).ConfigureAwait(false), Department.Create(name)),
        };
        if (taken || !await SaveAsync(entity, [], cancellationToken).ConfigureAwait(false))
        {
            return LookupWriteResult.Duplicate<LookupItem>(LookupMessages.NameTaken(kind));
        }

        return LookupWriteResult.Succeeded(ToItem(entity));
    }

    public async Task<LookupWriteResult<ModelItem>> CreateModelAsync(int brandId, string name, CancellationToken cancellationToken)
    {
        var brand = await db.Brands.SingleOrDefaultAsync(b => b.Id == brandId, cancellationToken).ConfigureAwait(false);
        if (brand is null || !brand.IsActive)
        {
            return LookupWriteResult.Invalid<ModelItem>(new Dictionary<string, string[]>
            {
                [nameof(CreateModelRequest.BrandId)] = [brand is null ? LookupMessages.BrandNotFound : LookupMessages.BrandInactive],
            });
        }

        var model = AssetModel.Create(brand, name);
        if (await db.AssetModels.AnyAsync(m => m.BrandId == brandId && m.Name == name, cancellationToken).ConfigureAwait(false)
            || !await SaveAsync(model, new() { ["brandId"] = brand.Id, ["brandName"] = brand.Name }, cancellationToken).ConfigureAwait(false))
        {
            return LookupWriteResult.Duplicate<ModelItem>(LookupMessages.ModelTaken);
        }

        return LookupWriteResult.Succeeded(ToItem(model));
    }

    public async Task<LookupWriteResult<LocationItem>> CreateLocationAsync(int cityId, string name, CancellationToken cancellationToken)
    {
        var city = await db.Cities.SingleOrDefaultAsync(c => c.Id == cityId, cancellationToken).ConfigureAwait(false);
        if (city is null || !city.IsActive)
        {
            return LookupWriteResult.Invalid<LocationItem>(new Dictionary<string, string[]>
            {
                [nameof(CreateLocationRequest.CityId)] = [city is null ? LookupMessages.CityNotFound : LookupMessages.CityInactive],
            });
        }

        var location = Location.Create(city, name);
        if (await db.Locations.AnyAsync(l => l.CityId == cityId && l.Name == name, cancellationToken).ConfigureAwait(false)
            || !await SaveAsync(location, new() { ["cityId"] = city.Id, ["cityName"] = city.Name }, cancellationToken).ConfigureAwait(false))
        {
            return LookupWriteResult.Duplicate<LocationItem>(LookupMessages.LocationTaken);
        }

        return LookupWriteResult.Succeeded(ToItem(location));
    }

    public async Task<LookupWriteResult<LookupItem>> UpdateAsync(
        LookupKind kind, int id, string name, bool isActive, byte[] rowVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rowVersion);

        // The name columns' Turkish_CI_AS collation makes the name checks case-insensitive, like the unique indexes.
        var (entity, nameTaken) = kind switch
        {
            LookupKind.Brand => (
                (ReferenceDataEntity?)await db.Brands.SingleOrDefaultAsync(b => b.Id == id, cancellationToken).ConfigureAwait(false),
                (Func<Task<bool>>)(() => db.Brands.AnyAsync(b => b.Id != id && b.Name == name, cancellationToken))),
            LookupKind.City => (
                await db.Cities.SingleOrDefaultAsync(c => c.Id == id, cancellationToken).ConfigureAwait(false),
                () => db.Cities.AnyAsync(c => c.Id != id && c.Name == name, cancellationToken)),
            _ => (
                await db.Departments.SingleOrDefaultAsync(d => d.Id == id, cancellationToken).ConfigureAwait(false),
                () => db.Departments.AnyAsync(d => d.Id != id && d.Name == name, cancellationToken)),
        };

        return await ChangeAsync(
                entity, new LookupEdit(name, isActive, rowVersion), nameTaken, LookupMessages.NameTaken(kind), inactiveParent: null, ToItem, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LookupWriteResult<ModelItem>> UpdateModelAsync(int id, string name, bool isActive, byte[] rowVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rowVersion);

        var model = await db.AssetModels.Include(m => m.Brand).SingleOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);
        return await ChangeAsync(
                model,
                new LookupEdit(name, isActive, rowVersion),
                () => db.AssetModels.AnyAsync(m => m.Id != id && m.BrandId == model!.BrandId && m.Name == name, cancellationToken),
                LookupMessages.ModelTaken,
                model is { Brand.IsActive: false } ? LookupMessages.ModelBrandInactive : null,
                ToItem,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LookupWriteResult<LocationItem>> UpdateLocationAsync(int id, string name, bool isActive, byte[] rowVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rowVersion);

        var location = await db.Locations.Include(l => l.City).SingleOrDefaultAsync(l => l.Id == id, cancellationToken).ConfigureAwait(false);
        return await ChangeAsync(
                location,
                new LookupEdit(name, isActive, rowVersion),
                () => db.Locations.AnyAsync(l => l.Id != id && l.CityId == location!.CityId && l.Name == name, cancellationToken),
                LookupMessages.LocationTaken,
                location is { City.IsActive: false } ? LookupMessages.LocationCityInactive : null,
                ToItem,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static LookupItem ToItem(ReferenceDataEntity lookup) =>
        new(lookup.Id, lookup.Name, lookup.IsActive, Convert.ToBase64String(lookup.RowVersion));

    private static ModelItem ToItem(AssetModel model) =>
        new(model.Id, model.Name, model.IsActive, model.BrandId, model.Brand.Name, Convert.ToBase64String(model.RowVersion));

    private static LocationItem ToItem(Location location) =>
        new(location.Id, location.Name, location.IsActive, location.CityId, location.City.Name, Convert.ToBase64String(location.RowVersion));

    /// <summary>
    /// Applies a rename, deactivation or reactivation and saves it with its <see cref="AuditAction.Updated"/> record in
    /// one <c>SaveChanges</c> (one transaction). The client's row version is the original value of the UPDATE, so a
    /// change saved by someone else after the caller read the lookup is refused, not overwritten.
    /// </summary>
    /// <param name="inactiveParent">Why the lookup cannot be reactivated (its brand or city is inactive), or <c>null</c>.</param>
    private async Task<LookupWriteResult<TItem>> ChangeAsync<TEntity, TItem>(
        TEntity? entity,
        LookupEdit edit,
        Func<Task<bool>> nameTaken,
        string nameTakenMessage,
        string? inactiveParent,
        Func<TEntity, TItem> toItem,
        CancellationToken cancellationToken)
        where TEntity : ReferenceDataEntity
        where TItem : class
    {
        if (entity is null)
        {
            return LookupWriteResult.NotFound<TItem>();
        }

        var entityName = entity.GetType().Name;
        if (!db.MatchesClientVersion(entity, edit.RowVersion))
        {
            LogConflict(entityName, entity.Id);
            return LookupWriteResult.Conflict<TItem>();
        }

        var oldValues = new Dictionary<string, object?>();
        var newValues = new Dictionary<string, object?>();

        // A change of letter case only ("dell" to "Dell") is a rename too.
        if (!string.Equals(entity.Name, edit.Name, StringComparison.Ordinal))
        {
            if (await nameTaken().ConfigureAwait(false))
            {
                return LookupWriteResult.Duplicate<TItem>(nameTakenMessage);
            }

            oldValues["name"] = entity.Name;
            entity.Rename(edit.Name);
            newValues["name"] = entity.Name;
        }

        if (entity.IsActive != edit.IsActive)
        {
            if (edit.IsActive && inactiveParent is not null)
            {
                db.ChangeTracker.Clear();
                return LookupWriteResult.Invalid<TItem>(new Dictionary<string, string[]> { [nameof(UpdateLookupRequest.IsActive)] = [inactiveParent] });
            }

            oldValues["isActive"] = entity.IsActive;
            if (edit.IsActive)
            {
                entity.Activate();
            }
            else
            {
                entity.Deactivate();
            }

            newValues["isActive"] = entity.IsActive;
        }

        if (newValues.Count == 0)
        {
            return LookupWriteResult.Succeeded(toItem(entity));
        }

        db.AuditLogs.Add(AuditLog.Create(
            entityName,
            entity.Id.ToString(CultureInfo.InvariantCulture),
            AuditAction.Updated,
            AssetAuditTrail.Serialize(oldValues),
            AssetAuditTrail.Serialize(newValues),
            currentUser.UserName ?? throw new InvalidOperationException("Lookups can only be changed by a signed-in user."),
            timeProvider.GetUtcNow(),
            requestContext.CorrelationId));
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            LogConflict(entityName, entity.Id);
            return LookupWriteResult.Conflict<TItem>();
        }
        catch (DbUpdateException ex) when (ex.IsUniqueKeyViolation())
        {
            db.ChangeTracker.Clear();
            return LookupWriteResult.Duplicate<TItem>(nameTakenMessage);
        }

        var fields = string.Join(", ", newValues.Keys);
        LogUpdated(entityName, entity.Id, fields);
        return LookupWriteResult.Succeeded(toItem(entity));
    }

    private static async Task<IReadOnlyList<LookupItem>> ListAsync<T>(IQueryable<T> lookups, CancellationToken cancellationToken)
        where T : ReferenceDataEntity =>
        await lookups.AsNoTracking()
            .OrderBy(l => l.Name)
            .ThenBy(l => l.Id)
            .Select(l => new LookupItem(l.Id, l.Name, l.IsActive, Convert.ToBase64String(l.RowVersion)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Inserts the lookup and its <see cref="AuditAction.Created"/> record in one transaction. <c>false</c> when a
    /// unique index refused the name: another request took it between the caller's check and the insert.
    /// </summary>
    private async Task<bool> SaveAsync(ReferenceDataEntity entity, Dictionary<string, object?> parent, CancellationToken cancellationToken)
    {
        var entityName = entity.GetType().Name;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            db.Add(entity);

            // Saved first so the audit record can name the lookup by its ID.
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            var values = new Dictionary<string, object?> { ["name"] = entity.Name, ["isActive"] = entity.IsActive };
            foreach (var (key, value) in parent)
            {
                values[key] = value;
            }

            db.AuditLogs.Add(AuditLog.Create(
                entityName,
                entity.Id.ToString(CultureInfo.InvariantCulture),
                AuditAction.Created,
                oldValues: null,
                AssetAuditTrail.Serialize(values),
                currentUser.UserName ?? throw new InvalidOperationException("Lookups can only be created by a signed-in user."),
                timeProvider.GetUtcNow(),
                requestContext.CorrelationId));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueKeyViolation())
        {
            db.ChangeTracker.Clear();
            return false;
        }

        LogCreated(entityName, entity.Id);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{EntityName} {EntityId} created")]
    private partial void LogCreated(string entityName, int entityId);

    [LoggerMessage(Level = LogLevel.Information, Message = "{EntityName} {EntityId} updated: {Fields}")]
    private partial void LogUpdated(string entityName, int entityId, string fields);

    [LoggerMessage(Level = LogLevel.Information, Message = "{EntityName} {EntityId} was changed by someone else; the update was refused")]
    private partial void LogConflict(string entityName, int entityId);

    /// <summary>What the caller asked for, and the version it read.</summary>
    private sealed record LookupEdit(string Name, bool IsActive, byte[] RowVersion);
}
