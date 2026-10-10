using EnterpriseInventory.Domain.Common;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnterpriseInventory.Infrastructure.Persistence.Configurations;

internal static class ConfigurationExtensions
{
    /// <summary>Audit columns and the rowversion concurrency token shared by every <see cref="AuditableEntity"/>.</summary>
    public static void ConfigureAuditable<T>(this EntityTypeBuilder<T> builder)
        where T : AuditableEntity
    {
        builder.Property(e => e.CreatedBy).HasMaxLength(AuditableEntity.UserNameMaxLength).IsRequired();
        builder.Property(e => e.UpdatedBy).HasMaxLength(AuditableEntity.UserNameMaxLength);
        builder.Property(e => e.RowVersion).IsRowVersion();
    }

    /// <summary>Lookup columns shared by every <see cref="ReferenceDataEntity"/>.</summary>
    public static void ConfigureReferenceData<T>(this EntityTypeBuilder<T> builder)
        where T : ReferenceDataEntity
    {
        builder.ConfigureAuditable();
        builder.Property(e => e.Name).HasMaxLength(ReferenceDataEntity.NameMaxLength).IsRequired();
    }

    /// <summary>SQL list of an enum's defined values, for IN (...) check constraints.</summary>
    public static string SqlValues<TEnum>()
        where TEnum : struct, Enum =>
        string.Join(", ", Enum.GetValues<TEnum>().Select(v => Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture)));
}
