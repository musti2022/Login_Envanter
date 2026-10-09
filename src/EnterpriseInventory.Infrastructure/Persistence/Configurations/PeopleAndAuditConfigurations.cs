using EnterpriseInventory.Domain.Auditing;
using EnterpriseInventory.Domain.Common;
using EnterpriseInventory.Domain.Employees;
using EnterpriseInventory.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnterpriseInventory.Infrastructure.Persistence.Configurations;

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ConfigureAuditable();
        builder.Property(e => e.SamAccountName).HasMaxLength(Employee.SamAccountNameMaxLength).IsRequired();
        builder.Property(e => e.DisplayName).HasMaxLength(Employee.DisplayNameMaxLength).IsRequired();
        builder.Property(e => e.Email).HasMaxLength(Employee.EmailMaxLength);
        builder.Property(e => e.Department).HasMaxLength(Employee.DepartmentMaxLength);
        builder.Property(e => e.Title).HasMaxLength(Employee.TitleMaxLength);

        builder.HasIndex(e => e.ObjectGuid).IsUnique();
        builder.HasIndex(e => e.SamAccountName);
        builder.HasIndex(e => e.DisplayName);
    }
}

internal sealed class AdminUserConfiguration : IEntityTypeConfiguration<AdminUser>
{
    public void Configure(EntityTypeBuilder<AdminUser> builder)
    {
        builder.Property(u => u.SamAccountName).HasMaxLength(AdminUser.SamAccountNameMaxLength).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(AdminUser.DisplayNameMaxLength).IsRequired();
        builder.HasIndex(u => u.ObjectGuid).IsUnique();
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.Property(l => l.EntityName).HasMaxLength(AuditLog.EntityNameMaxLength).IsRequired();
        builder.Property(l => l.EntityId).HasMaxLength(AuditLog.EntityIdMaxLength).IsRequired();
        builder.Property(l => l.UserName).HasMaxLength(AuditableEntity.UserNameMaxLength).IsRequired();
        builder.Property(l => l.CorrelationId).HasMaxLength(AuditLog.CorrelationIdMaxLength).IsRequired();

        builder.HasIndex(l => new { l.EntityName, l.EntityId });
        builder.HasIndex(l => l.Timestamp);
        builder.HasIndex(l => l.CorrelationId);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_AuditLogs_Action", $"[Action] IN ({ConfigurationExtensions.SqlValues<AuditAction>()})");
            t.HasCheckConstraint("CK_AuditLogs_HasValues", "[OldValues] IS NOT NULL OR [NewValues] IS NOT NULL");
        });
    }
}
