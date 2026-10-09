using EnterpriseInventory.Domain.Assets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnterpriseInventory.Infrastructure.Persistence.Configurations;

internal sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ConfigureAuditable();

        builder.Property(a => a.AssetCode).HasMaxLength(Asset.AssetCodeMaxLength).IsRequired();
        builder.Property(a => a.ComputerName).HasMaxLength(Asset.ComputerNameMaxLength);
        builder.Property(a => a.SerialNumber).HasMaxLength(Asset.SerialNumberMaxLength);
        builder.Property(a => a.Description).HasMaxLength(Asset.DescriptionMaxLength);

        builder.HasOne(a => a.Brand)
            .WithMany()
            .HasForeignKey(a => a.BrandId)
            .OnDelete(DeleteBehavior.Restrict);

        // (ModelId, BrandId) -> AssetModels(Id, BrandId): the brand always matches the model.
        builder.HasOne(a => a.Model)
            .WithMany()
            .HasForeignKey(a => new { a.ModelId, a.BrandId })
            .HasPrincipalKey(m => new { m.Id, m.BrandId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.City)
            .WithMany()
            .HasForeignKey(a => a.CityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Department)
            .WithMany()
            .HasForeignKey(a => a.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        // (LocationId, CityId) -> Locations(Id, CityId): the location is in the asset's city.
        // SQL Server skips the check when LocationId is NULL, so the location stays optional.
        builder.HasOne(a => a.Location)
            .WithMany()
            .HasForeignKey(a => new { a.LocationId, a.CityId })
            .HasPrincipalKey(l => new { l.Id, l.CityId })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(a => a.Assignments)
            .WithOne(x => x.Asset)
            .HasForeignKey(x => x.AssetId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(a => a.Assignments).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(a => a.ActiveAssignment);

        builder.HasIndex(a => a.AssetCode).IsUnique();
        builder.HasIndex(a => a.SerialNumber).IsUnique().HasFilter("[SerialNumber] IS NOT NULL");
        builder.HasIndex(a => new { a.IsDeleted, a.Status });

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Assets_Status", $"[Status] IN ({ConfigurationExtensions.SqlValues<AssetStatus>()})");
            t.HasCheckConstraint("CK_Assets_AssetType", $"[AssetType] IN ({ConfigurationExtensions.SqlValues<AssetType>()})");
            t.HasCheckConstraint("CK_Assets_AssetCode_NotBlank", "LEN([AssetCode]) > 0");
            t.HasCheckConstraint("CK_Assets_SerialNumber_NotBlank", "[SerialNumber] IS NULL OR LEN([SerialNumber]) > 0");
            t.HasCheckConstraint("CK_Assets_ArchivedNotAssigned", $"NOT ([IsDeleted] = 1 AND [Status] = {(int)AssetStatus.Assigned})");
        });
    }
}

internal sealed class AssetAssignmentConfiguration : IEntityTypeConfiguration<AssetAssignment>
{
    public void Configure(EntityTypeBuilder<AssetAssignment> builder)
    {
        builder.Property(x => x.AssignmentDescription).HasMaxLength(AssetAssignment.AssignmentDescriptionMaxLength);
        builder.Property(x => x.Notes).HasMaxLength(AssetAssignment.NotesMaxLength);
        builder.Property(x => x.AssignedBy).HasMaxLength(Domain.Common.AuditableEntity.UserNameMaxLength).IsRequired();
        builder.Property(x => x.ReturnedBy).HasMaxLength(Domain.Common.AuditableEntity.UserNameMaxLength);
        builder.Ignore(x => x.IsActive);

        builder.HasOne(x => x.Employee)
            .WithMany()
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        // At most one active (not returned) assignment per asset, enforced even under concurrent requests.
        builder.HasIndex(x => x.AssetId)
            .IsUnique()
            .HasFilter("[ReturnedAt] IS NULL")
            .HasDatabaseName("UX_AssetAssignments_AssetId_Active");
        builder.HasIndex(x => new { x.AssetId, x.AssignedAt });

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_AssetAssignments_ReturnAfterAssign", "[ReturnedAt] IS NULL OR [ReturnedAt] >= [AssignedAt]");
            t.HasCheckConstraint(
                "CK_AssetAssignments_ReturnedByWithReturn",
                "([ReturnedAt] IS NULL AND [ReturnedBy] IS NULL) OR ([ReturnedAt] IS NOT NULL AND [ReturnedBy] IS NOT NULL)");
        });
    }
}
