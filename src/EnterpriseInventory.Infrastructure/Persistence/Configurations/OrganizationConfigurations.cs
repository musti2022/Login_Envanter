using EnterpriseInventory.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnterpriseInventory.Infrastructure.Persistence.Configurations;

internal sealed class CityConfiguration : IEntityTypeConfiguration<City>
{
    public void Configure(EntityTypeBuilder<City> builder)
    {
        builder.ToTable("Cities").HasKey(c => c.Id);
        builder.ConfigureReferenceData();
        builder.HasIndex(c => c.Name).IsUnique();
    }
}

internal sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("Departments").HasKey(d => d.Id);
        builder.ConfigureReferenceData();
        builder.HasIndex(d => d.Name).IsUnique();
    }
}

internal sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("Locations").HasKey(l => l.Id);
        builder.ConfigureReferenceData();

        builder.HasOne(l => l.City)
            .WithMany()
            .HasForeignKey(l => l.CityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => new { l.CityId, l.Name }).IsUnique();

        // Target of the composite foreign key from Assets, which guarantees that an asset's
        // location is in the asset's city.
        builder.HasAlternateKey(l => new { l.Id, l.CityId });
    }
}
