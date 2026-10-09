using System.Net;
using EnterpriseInventory.Infrastructure.ActiveDirectory;
using EnterpriseInventory.IntegrationTests.ActiveDirectory;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseInventory.IntegrationTests.Assets;

/// <summary>
/// Days 21 and 22 against the Samba AD test domain: an employee who is not a member of Bim_Envanter (and so can
/// never sign in) is found and given an asset; a disabled account is refused when the assignment re-reads it.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class AssignmentWithActiveDirectoryTests(SqlServerDatabaseFixture fixture)
{
    [ActiveDirectoryAndSqlServerFact]
    public async Task A_directory_user_outside_the_allowed_group_is_given_an_asset()
    {
        var refs = await InventoryReferences.SeedAsync(fixture);
        await using var api = new TestApiFactory(fixture.ConnectionString, settings: TestActiveDirectory.Settings());
        using var client = api.CreateSignedInClient("ayse.admin");
        var asset = await client.CreateAssetAsync(refs.NewAssetBody());

        var assigned = await client.AssignAsync(asset, await client.EmployeeGuidAsync("mehmet.user"), "Dizüstü bilgisayar");

        var holder = assigned.GetProperty("activeAssignment");
        Assert.Equal("mehmet.user", holder.GetProperty("userName").GetString());
        Assert.Equal("Mehmet Öztürk", holder.GetProperty("displayName").GetString());
        await using var db = fixture.CreateContext();
        var employee = await db.Employees.SingleAsync(e => e.Id == holder.GetProperty("employeeId").GetInt32());
        Assert.Equal($"mehmet.user@{TestActiveDirectory.Options().Domain}", employee.Email);
        Assert.False(await db.AdminUsers.AnyAsync(u => u.ObjectGuid == employee.ObjectGuid));
    }

    [ActiveDirectoryAndSqlServerFact]
    public async Task A_disabled_directory_account_is_refused_when_the_assignment_reads_it_again()
    {
        var refs = await InventoryReferences.SeedAsync(fixture);
        await using var api = new TestApiFactory(fixture.ConnectionString, settings: TestActiveDirectory.Settings());
        using var client = api.CreateSignedInClient("ayse.admin");
        var asset = await client.CreateAssetAsync(refs.NewAssetBody());
        var options = TestActiveDirectory.Options();
        using var factory = new LdapConnectionFactory(
            Microsoft.Extensions.Options.Options.Create(options), Microsoft.Extensions.Logging.Abstractions.NullLogger<LdapConnectionFactory>.Instance);
        using var connection = await factory.ConnectAsync(CancellationToken.None);
        await connection.BindAsync(options.ServiceAccountBindName, options.ServiceAccountPassword, CancellationToken.None);
        var entry = Assert.Single((await connection.SearchAsync(
            options.BaseDn, Novell.Directory.Ldap.LdapConnection.ScopeSub, "(sAMAccountName=disabled.user)", ["objectGUID"], CancellationToken.None)).Entries);

        using var response = await client.PostAssignmentAsync(asset.Id(), asset.AssignBody(new Guid(entry.GetAttributeSet().Find("objectGUID")!.ByteValue)));

        Assert.Equal("employee_inactive", (await AssetApi.ReadAsync(response, HttpStatusCode.Conflict)).Code());
    }
}
