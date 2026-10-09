using EnterpriseInventory.Domain.Auditing;
using EnterpriseInventory.Domain.Common;
using EnterpriseInventory.Domain.Employees;
using EnterpriseInventory.Domain.Users;
using static EnterpriseInventory.UnitTests.DomainModel.TestData;

namespace EnterpriseInventory.UnitTests.DomainModel;

public class AuditableEntityTests
{
    [Fact]
    public void MarkCreated_and_MarkUpdated_stamp_user_and_time()
    {
        var asset = NewAsset();

        asset.MarkCreated(" creator ", Now);
        asset.MarkUpdated("editor", Now.AddHours(1));

        Assert.Equal("creator", asset.CreatedBy);
        Assert.Equal(Now, asset.CreatedAt);
        Assert.Equal("editor", asset.UpdatedBy);
        Assert.Equal(Now.AddHours(1), asset.UpdatedAt);
    }

    [Fact]
    public void A_new_entity_has_no_update_stamp()
    {
        var asset = NewAsset();
        asset.MarkCreated("creator", Now);

        Assert.Null(asset.UpdatedAt);
        Assert.Null(asset.UpdatedBy);
    }

    [Fact]
    public void MarkUpdated_rejects_a_time_before_creation()
    {
        var asset = NewAsset();
        asset.MarkCreated("creator", Now);

        DomainAssert.Violates(DomainErrors.Audit.UpdatedBeforeCreated, () => asset.MarkUpdated("editor", Now.AddSeconds(-1)));
        Assert.Null(asset.UpdatedAt);
    }

    [Fact]
    public void Audit_stamps_require_a_user()
    {
        var asset = NewAsset();

        DomainAssert.Violates(DomainErrors.Required, () => asset.MarkCreated("", Now));
        DomainAssert.Violates(DomainErrors.Required, () => asset.MarkUpdated(" ", Now));
    }
}

public class EmployeeTests
{
    [Fact]
    public void Create_copies_directory_profile()
    {
        var objectGuid = Guid.NewGuid();

        var employee = Employee.Create(objectGuid, " mehmet.kaya ", "Mehmet Kaya", null, "Satış", null, isActive: true, Now);

        Assert.Equal(objectGuid, employee.ObjectGuid);
        Assert.Equal("mehmet.kaya", employee.SamAccountName);
        Assert.Equal("Mehmet Kaya", employee.DisplayName);
        Assert.Null(employee.Email);
        Assert.Equal("Satış", employee.Department);
        Assert.True(employee.IsActive);
        Assert.Equal(Now, employee.LastSyncedAt);
    }

    [Fact]
    public void Create_requires_the_directory_object_guid()
    {
        DomainAssert.Violates(DomainErrors.Required, () =>
            Employee.Create(Guid.Empty, "mehmet.kaya", "Mehmet Kaya", null, null, null, true, Now));
    }

    [Fact]
    public void SamAccountName_is_limited_to_the_directory_maximum_of_20_characters()
    {
        DomainAssert.Violates(DomainErrors.TooLong, () =>
            Employee.Create(Guid.NewGuid(), new string('a', 21), "Uzun Ad", null, null, null, true, Now));
    }

    [Fact]
    public void UpdateFromDirectory_refreshes_profile_and_active_flag()
    {
        var employee = NewEmployee();

        employee.UpdateFromDirectory("ayse.demir", "Ayşe Demir", "ayse.demir@example.local", "Finans", "Müdür", isActive: false, Now.AddDays(1));

        Assert.Equal("ayse.demir", employee.SamAccountName);
        Assert.Equal("Ayşe Demir", employee.DisplayName);
        Assert.Equal("Finans", employee.Department);
        Assert.False(employee.IsActive);
        Assert.Equal(Now.AddDays(1), employee.LastSyncedAt);
    }

    [Fact]
    public void Rejected_directory_update_leaves_the_record_unchanged()
    {
        var employee = NewEmployee();

        DomainAssert.Violates(DomainErrors.Required, () =>
            employee.UpdateFromDirectory("new.name", " ", null, null, null, isActive: false, Now.AddDays(1)));

        Assert.Equal("ayse.yilmaz", employee.SamAccountName);
        Assert.True(employee.IsActive);
    }
}

public class AdminUserTests
{
    [Fact]
    public void Create_records_first_and_last_login()
    {
        var user = AdminUser.Create(Guid.NewGuid(), "admin.user", "Admin Kullanıcı", Now);

        Assert.Equal(Now, user.FirstLoginAt);
        Assert.Equal(Now, user.LastLoginAt);
        Assert.Equal("admin.user", user.SamAccountName);
    }

    [Fact]
    public void RecordLogin_updates_names_and_last_login_only()
    {
        var user = AdminUser.Create(Guid.NewGuid(), "admin.user", "Admin Kullanıcı", Now);

        user.RecordLogin("admin.user", "Yeni Ad", Now.AddDays(3));

        Assert.Equal(Now, user.FirstLoginAt);
        Assert.Equal(Now.AddDays(3), user.LastLoginAt);
        Assert.Equal("Yeni Ad", user.DisplayName);
    }

    [Fact]
    public void Admin_users_and_employees_are_separate_types()
    {
        Assert.False(typeof(Employee).IsAssignableFrom(typeof(AdminUser)));
        Assert.False(typeof(AdminUser).IsAssignableFrom(typeof(Employee)));
    }

    [Fact]
    public void Admin_user_has_no_credential_fields()
    {
        var names = typeof(AdminUser).GetProperties().Select(p => p.Name.ToUpperInvariant());

        Assert.DoesNotContain(names, n => n.Contains("PASSWORD", StringComparison.Ordinal) || n.Contains("TOKEN", StringComparison.Ordinal) || n.Contains("SECRET", StringComparison.Ordinal));
    }
}

public class AuditLogTests
{
    [Fact]
    public void Create_records_who_did_what_and_when()
    {
        var log = AuditLog.Create("Asset", "42", AuditAction.Updated, "{\"Status\":1}", "{\"Status\":3}", "admin.user", Now, "corr-123");

        Assert.Equal("Asset", log.EntityName);
        Assert.Equal("42", log.EntityId);
        Assert.Equal(AuditAction.Updated, log.Action);
        Assert.Equal("{\"Status\":1}", log.OldValues);
        Assert.Equal("{\"Status\":3}", log.NewValues);
        Assert.Equal("admin.user", log.UserName);
        Assert.Equal(Now, log.Timestamp);
        Assert.Equal("corr-123", log.CorrelationId);
    }

    [Fact]
    public void Create_needs_old_or_new_values()
    {
        DomainAssert.Violates(DomainErrors.Required, () =>
            AuditLog.Create("Asset", "42", AuditAction.Updated, " ", null, "admin.user", Now, "corr-123"));
    }

    [Theory]
    [InlineData("", "42", "admin.user", "corr")]
    [InlineData("Asset", "", "admin.user", "corr")]
    [InlineData("Asset", "42", "", "corr")]
    [InlineData("Asset", "42", "admin.user", "")]
    public void Create_requires_entity_user_and_correlation_id(string entityName, string entityId, string userName, string correlationId)
    {
        DomainAssert.Violates(DomainErrors.Required, () =>
            AuditLog.Create(entityName, entityId, AuditAction.Created, null, "{}", userName, Now, correlationId));
    }

    [Fact]
    public void Create_rejects_undefined_actions()
    {
        DomainAssert.Violates(DomainErrors.InvalidValue, () =>
            AuditLog.Create("Asset", "42", (AuditAction)0, null, "{}", "admin.user", Now, "corr"));
    }

    [Fact]
    public void Audit_log_is_append_only()
    {
        var setters = typeof(AuditLog).GetProperties().Where(p => p.SetMethod?.IsPublic == true);
        var mutators = typeof(AuditLog).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

        Assert.Empty(setters);
        Assert.Empty(mutators);
    }
}
