using EnterpriseInventory.Application.Authentication;
using EnterpriseInventory.Infrastructure.ActiveDirectory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Novell.Directory.Ldap;

namespace EnterpriseInventory.IntegrationTests.ActiveDirectory;

/// <summary>
/// The periodic access re-check of signed-in users (day 9) against the Samba AD test domain: with the service
/// account, by objectGUID, without the user's password.
/// </summary>
public sealed class SambaAccessCheckTests
{
    [ActiveDirectoryTheory]
    [InlineData("ayse.admin", NestedGroupPolicy.DirectMembershipOnly, DirectoryAccessStatus.Allowed)]
    [InlineData("ayse.admin", NestedGroupPolicy.IncludeNested, DirectoryAccessStatus.Allowed)]
    [InlineData("primary.user", NestedGroupPolicy.DirectMembershipOnly, DirectoryAccessStatus.Allowed)]
    [InlineData("nested.user", NestedGroupPolicy.IncludeNested, DirectoryAccessStatus.Allowed)]
    [InlineData("nested.user", NestedGroupPolicy.DirectMembershipOnly, DirectoryAccessStatus.NotAuthorized)]
    [InlineData("mehmet.user", NestedGroupPolicy.DirectMembershipOnly, DirectoryAccessStatus.NotAuthorized)]
    [InlineData("decoy.user", NestedGroupPolicy.IncludeNested, DirectoryAccessStatus.NotAuthorized)]
    [InlineData("disabled.user", NestedGroupPolicy.DirectMembershipOnly, DirectoryAccessStatus.AccountDisabled)]
    [InlineData("expired.user", NestedGroupPolicy.DirectMembershipOnly, DirectoryAccessStatus.AccountExpired)]
    public async Task The_service_account_re_checks_a_users_access(string userName, NestedGroupPolicy policy, DirectoryAccessStatus expected)
    {
        var options = TestActiveDirectory.Options(policy);
        var objectGuid = await ObjectGuidOf(userName);

        var status = await CheckAccess(options, objectGuid);

        Assert.Equal(expected, status);
    }

    [ActiveDirectoryFact]
    public async Task A_user_who_signed_in_is_found_again_by_the_identity_recorded_at_sign_in()
    {
        var options = TestActiveDirectory.Options();
        using var factory = Factory(options);
        var service = Service(factory, options);
        var signIn = await service.SignInAsync("ayse.admin", TestActiveDirectory.UserPassword, CancellationToken.None);

        var status = await service.CheckAccessAsync(signIn.Account!.ObjectGuid, CancellationToken.None);

        Assert.Equal(DirectoryAccessStatus.Allowed, status);
    }

    [ActiveDirectoryFact]
    public async Task An_account_that_is_gone_or_outside_base_dn_is_not_found()
    {
        var options = TestActiveDirectory.Options();
        var ayse = await ObjectGuidOf("ayse.admin");

        Assert.Equal(DirectoryAccessStatus.AccountNotFound, await CheckAccess(options, Guid.NewGuid()));

        options.BaseDn = "OU=Sahte," + options.BaseDn;
        Assert.Equal(DirectoryAccessStatus.AccountNotFound, await CheckAccess(options, ayse));
    }

    [ActiveDirectoryFact]
    public async Task A_wrong_service_account_password_means_access_cannot_be_confirmed()
    {
        var options = TestActiveDirectory.Options();
        options.ServiceAccountPassword = "Wrong-Password-1";
        var logger = new CapturingLogger<LdapDirectoryService>();
        using var factory = Factory(options);

        var status = await Service(factory, options, logger).CheckAccessAsync(await ObjectGuidOf("ayse.admin"), CancellationToken.None);

        Assert.Equal(DirectoryAccessStatus.DirectoryUnavailable, status);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("refused the service account", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("Wrong-Password-1", StringComparison.Ordinal));
    }

    private static async Task<DirectoryAccessStatus> CheckAccess(ActiveDirectoryOptions options, Guid objectGuid)
    {
        using var factory = Factory(options);
        return await Service(factory, options).CheckAccessAsync(objectGuid, CancellationToken.None);
    }

    /// <summary>The user's objectGUID, read with the service account (disabled and expired users cannot sign in).</summary>
    private static async Task<Guid> ObjectGuidOf(string userName)
    {
        var options = TestActiveDirectory.Options();
        using var factory = Factory(options);
        using var connection = await factory.ConnectAsync(CancellationToken.None);
        await connection.BindAsync($"{options.ServiceAccountUserName}@{options.Domain}", options.ServiceAccountPassword, CancellationToken.None);
        var result = await connection.SearchAsync(
            options.BaseDn, LdapConnection.ScopeSub, $"(sAMAccountName={userName})", ["objectGUID"], CancellationToken.None);
        return new Guid(Assert.Single(result.Entries).GetAttributeSet().Find("objectGUID")!.ByteValue);
    }

    private static LdapConnectionFactory Factory(ActiveDirectoryOptions options) =>
        new(Microsoft.Extensions.Options.Options.Create(options), NullLogger<LdapConnectionFactory>.Instance);

    private static LdapDirectoryService Service(LdapConnectionFactory factory, ActiveDirectoryOptions options, ILogger<LdapDirectoryService>? logger = null) =>
        new(factory, Microsoft.Extensions.Options.Options.Create(options), TimeProvider.System, logger ?? NullLogger<LdapDirectoryService>.Instance);
}
