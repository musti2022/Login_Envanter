using EnterpriseInventory.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnterpriseInventory.Infrastructure.Persistence.Configurations;

internal sealed class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        builder.ToTable("Brands").HasKey(b => b.Id);
        builder.ConfigureReferenceData();
        builder.HasIndex(b => b.Name).IsUnique();
    }
}

internal sealed class AssetModelConfiguration : IEntityTypeConfiguration<AssetModel>
{
    public void Configure(EntityTypeBuilder<AssetModel> builder)
    {
        builder.ToTable("AssetModels").HasKey(m => m.Id);
        builder.ConfigureReferenceData();

        builder.HasOne(m => m.Brand)
            .WithMany()
            .HasForeignKey(m => m.BrandId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => new { m.BrandId, m.Name }).IsUnique();

        // Target of the composite foreign key from Assets, which guarantees that an asset's
        // BrandId is the brand of its model.
        builder.HasAlternateKey(m => new { m.Id, m.BrandId });
    }
}
