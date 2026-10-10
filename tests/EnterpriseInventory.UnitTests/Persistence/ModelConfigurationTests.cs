using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Domain.Auditing;
using EnterpriseInventory.Domain.Catalog;
using EnterpriseInventory.Domain.Common;
using EnterpriseInventory.Domain.Organization;
using EnterpriseInventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EnterpriseInventory.UnitTests.Persistence;

/// <summary>
/// The EF Core model as the Fluent API configurations define it (no database needed): every table, key, length,
/// relationship and delete behavior is configured on purpose rather than left to conventions.
/// </summary>
public sealed class ModelConfigurationTests : IDisposable
{
    private readonly ApplicationDbContext _context = new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer("Server=unused.invalid;Database=unused").Options);

    /// <summary>The design-time model keeps how each part was configured (explicitly or by convention).</summary>
    private IModel Model => _context.GetService<IDesignTimeModel>().Model;

    private IEnumerable<IConventionEntityType> EntityTypes => Model.GetEntityTypes().Cast<IConventionEntityType>();

    public void Dispose() => _context.Dispose();

    [Fact]
    public void Every_entity_has_an_explicit_table_name_and_primary_key()
    {
        Assert.All(EntityTypes, entityType =>
        {
            Assert.Equal(ConfigurationSource.Explicit, entityType.GetTableNameConfigurationSource());
            Assert.Equal(ConfigurationSource.Explicit, ((IConventionKey)entityType.FindPrimaryKey()!).GetConfigurationSource());
        });
        Assert.Equal(
            ["AdminUsers", "AssetAssignments", "AssetModels", "Assets", "AuditLogs", "Brands", "Cities", "Departments", "Employees", "Locations", "UserSessions"],
            EntityTypes.Select(e => e.GetTableName()!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void No_relationship_cascades_so_deleting_a_row_never_takes_assignments_or_history_with_it()
    {
        var foreignKeys = EntityTypes.SelectMany(e => e.GetForeignKeys()).ToList();

        Assert.NotEmpty(foreignKeys);
        Assert.All(foreignKeys, fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
    }

    [Fact]
    public void Every_text_column_has_a_maximum_length_except_the_audit_snapshots()
    {
        var unbounded = EntityTypes
            .SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(string) && p.GetMaxLength() is null)
            .ToList();

        Assert.Equal(
            [$"{nameof(AuditLog)}.{nameof(AuditLog.NewValues)}", $"{nameof(AuditLog)}.{nameof(AuditLog.OldValues)}"],
            unbounded.Select(p => $"{p.DeclaringType.ClrType.Name}.{p.Name}").Order(StringComparer.Ordinal));
        Assert.All(unbounded, p =>
        {
            Assert.Equal("nvarchar(max)", p.GetColumnType());
            Assert.Equal(ConfigurationSource.Explicit, ((IConventionProperty)p).GetColumnTypeConfigurationSource());
        });
    }

    [Fact]
    public void Audited_entities_carry_a_rowversion_concurrency_token()
    {
        var audited = EntityTypes.Where(e => typeof(AuditableEntity).IsAssignableFrom(e.ClrType)).ToList();

        Assert.Contains(audited, e => e.ClrType == typeof(Asset));
        Assert.All(audited, entityType =>
        {
            var rowVersion = entityType.FindProperty(nameof(AuditableEntity.RowVersion))!;
            Assert.Equal(typeof(byte[]), rowVersion.ClrType);
            Assert.True(rowVersion.IsConcurrencyToken);
            Assert.Equal(ValueGenerated.OnAddOrUpdate, rowVersion.ValueGenerated);
            Assert.Equal("rowversion", rowVersion.GetColumnType());
        });
    }

    [Fact]
    public void An_assets_brand_comes_from_its_model_and_its_location_from_its_city()
    {
        var asset = Model.FindEntityType(typeof(Asset))!;

        var model = Assert.Single(asset.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(AssetModel));
        Assert.Equal([nameof(Asset.ModelId), nameof(Asset.BrandId)], model.Properties.Select(p => p.Name));
        Assert.Equal([nameof(AssetModel.Id), nameof(AssetModel.BrandId)], model.PrincipalKey.Properties.Select(p => p.Name));

        var location = Assert.Single(asset.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(Location));
        Assert.Equal([nameof(Asset.LocationId), nameof(Asset.CityId)], location.Properties.Select(p => p.Name));
        Assert.Equal([nameof(Location.Id), nameof(Location.CityId)], location.PrincipalKey.Properties.Select(p => p.Name));

        var brand = Assert.Single(Model.FindEntityType(typeof(AssetModel))!.GetForeignKeys());
        Assert.Equal(typeof(Brand), brand.PrincipalEntityType.ClrType);
        Assert.True(brand.IsRequired);
    }

    [Fact]
    public void Codes_serial_numbers_and_active_assignments_are_unique()
    {
        var asset = Model.FindEntityType(typeof(Asset))!;
        var assignment = Model.FindEntityType(typeof(AssetAssignment))!;

        var code = Assert.Single(asset.GetIndexes(), i => i.Properties is [{ Name: nameof(Asset.AssetCode) }]);
        Assert.True(code.IsUnique);
        Assert.Null(code.GetFilter());
        var serial = Assert.Single(asset.GetIndexes(), i => i.Properties is [{ Name: nameof(Asset.SerialNumber) }]);
        Assert.True(serial.IsUnique);
        Assert.Equal("[SerialNumber] IS NOT NULL", serial.GetFilter());
        var active = Assert.Single(assignment.GetIndexes(), i => i.GetDatabaseName() == "UX_AssetAssignments_AssetId_Active");
        Assert.True(active.IsUnique);
        Assert.Equal("[ReturnedAt] IS NULL", active.GetFilter());
    }

    [Fact]
    public void Only_assets_and_their_assignments_have_the_soft_delete_filter()
    {
        var filtered = EntityTypes
            .Where(e => e.GetDeclaredQueryFilters().Count > 0)
            .ToDictionary(e => e.ClrType, e => e.GetDeclaredQueryFilters().Select(f => f.Key).ToList());

        Assert.Equal([typeof(Asset), typeof(AssetAssignment)], filtered.Keys.OrderBy(t => t.Name, StringComparer.Ordinal));
        Assert.All(filtered.Values, keys => Assert.Equal([SoftDelete.FilterName], keys));
    }

    /// <summary>
    /// Lazy loading needs either the proxies package or entities that take an ILazyLoader; Infrastructure does not
    /// reference the first and the Domain cannot (it has no EF Core reference, see LayerDependencyTests).
    /// </summary>
    [Fact]
    public void Lazy_loading_is_off()
    {
        Assert.DoesNotContain(
            typeof(ApplicationDbContext).Assembly.GetReferencedAssemblies(),
            a => a.Name == "Microsoft.EntityFrameworkCore.Proxies");
        Assert.DoesNotContain(
            typeof(Asset).Assembly.GetReferencedAssemblies(),
            a => a.Name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }
}
