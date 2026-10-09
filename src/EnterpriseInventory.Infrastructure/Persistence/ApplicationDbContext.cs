using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Domain.Auditing;
using EnterpriseInventory.Domain.Catalog;
using EnterpriseInventory.Domain.Employees;
using EnterpriseInventory.Domain.Organization;
using EnterpriseInventory.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseInventory.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Asset> Assets => Set<Asset>();

    public DbSet<AssetAssignment> AssetAssignments => Set<AssetAssignment>();

    public DbSet<Brand> Brands => Set<Brand>();

    public DbSet<AssetModel> AssetModels => Set<AssetModel>();

    public DbSet<City> Cities => Set<City>();

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();

    public DbSet<UserSession> UserSessions => Set<UserSession>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        TouchChangedAssets();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        TouchChangedAssets();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }

    /// <summary>
    /// Assignments belong to the <see cref="Asset"/> aggregate and have no row version of their own. When one is
    /// added or changed, the asset row is updated too, so the asset's RowVersion rejects concurrent changes to
    /// the aggregate and its UpdatedAt/UpdatedBy are stamped. Without this a handover (return and reassign in
    /// one save) would leave the asset row untouched, because its status ends where it started.
    /// </summary>
    private void TouchChangedAssets()
    {
        var changedAssets = ChangeTracker.Entries<AssetAssignment>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .Select(e => Entry(e.Entity.Asset))
            .Where(e => e.State == EntityState.Unchanged)
            .ToList();
        foreach (var asset in changedAssets)
        {
            asset.State = EntityState.Modified;
        }
    }
}
