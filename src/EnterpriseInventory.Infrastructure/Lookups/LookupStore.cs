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
            .Select(m => new ModelItem(m.Id, m.Name, m.IsActive, m.BrandId, m.Brand.Name))
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
            .Select(l => new LocationItem(l.Id, l.Name, l.IsActive, l.CityId, l.City.Name))
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

        return LookupWriteResult.Succeeded(new LookupItem(entity.Id, entity.Name, entity.IsActive));
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

        return LookupWriteResult.Succeeded(new ModelItem(model.Id, model.Name, model.IsActive, brand.Id, brand.Name));
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

        return LookupWriteResult.Succeeded(new LocationItem(location.Id, location.Name, location.IsActive, city.Id, city.Name));
    }

    private static async Task<IReadOnlyList<LookupItem>> ListAsync<T>(IQueryable<T> lookups, CancellationToken cancellationToken)
        where T : ReferenceDataEntity =>
        await lookups.AsNoTracking()
            .OrderBy(l => l.Name)
            .ThenBy(l => l.Id)
            .Select(l => new LookupItem(l.Id, l.Name, l.IsActive))
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
}
