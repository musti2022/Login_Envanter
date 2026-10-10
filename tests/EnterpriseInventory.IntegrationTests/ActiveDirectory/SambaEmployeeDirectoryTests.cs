using EnterpriseInventory.Application.Employees;
using EnterpriseInventory.Infrastructure.ActiveDirectory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EnterpriseInventory.IntegrationTests.ActiveDirectory;

/// <summary>
/// Finding employees to give assets to (day 21) in the Samba AD test domain, with the service account. Anyone
/// with an enabled account can be found, whether or not they are a member of Bim_Envanter.
/// </summary>
public sealed class SambaEmployeeDirectoryTests
{
    [ActiveDirectoryFact]
    public async Task A_user_outside_the_allowed_group_is_found_with_their_profile()
    {
        var result = await Search("mehmet");

        Assert.Equal(DirectoryLookupStatus.Succeeded, result.Status);
        var mehmet = Assert.Single(result.People);
        Assert.Equal("mehmet.user", mehmet.SamAccountName);
        Assert.Equal($"mehmet.user@{TestActiveDirectory.Options().Domain}", mehmet.Email);
        Assert.True(mehmet.IsEnabled);
        Assert.False(result.HasMore);
    }

    [ActiveDirectoryTheory]
    [InlineData("Öztürk")]
    [InlineData("mehmet öz")]
    [InlineData("MEHMET.U")]
    [InlineData("mehmet.user@")]
    public async Task Each_word_matches_the_start_of_a_logon_name_name_or_e_mail(string term)
    {
        var result = await Search(term);

        Assert.Equal("mehmet.user", Assert.Single(result.People).SamAccountName);
    }

    [ActiveDirectoryTheory]
    [InlineData("hmet")]
    [InlineData("mehmet ayşe")]
    public async Task A_word_that_starts_no_name_finds_nobody(string term)
    {
        var result = await Search(term);

        Assert.Equal(DirectoryLookupStatus.Succeeded, result.Status);
        Assert.Empty(result.People);
    }

    [ActiveDirectoryFact]
    public async Task Disabled_accounts_are_left_out()
    {
        // disabled.user is "Pasif Kullanıcı"; nested.user, also "Kullanıcı", is enabled.
        var result = await Search("kullanıcı");

        Assert.Equal(["nested.user"], result.People.Select(p => p.SamAccountName));
    }

    [ActiveDirectoryFact]
    public async Task Filter_characters_typed_by_the_user_are_searched_for_literally()
    {
        var result = await Search("*)(sAMAccountName=*");

        Assert.Equal(DirectoryLookupStatus.Succeeded, result.Status);
        Assert.Empty(result.People);
    }

    [ActiveDirectoryFact]
    public async Task More_matches_than_the_limit_are_reported()
    {
        // clash.member and clash.other are both "Çakışan".
        var all = await Search("çakışan", limit: 5);
        var limited = await Search("çakışan", limit: 1);

        Assert.Equal(["clash.member", "clash.other"], all.People.Select(p => p.SamAccountName).Order());
        Assert.False(all.HasMore);
        Assert.Single(limited.People);
        Assert.True(limited.HasMore);
    }

    [ActiveDirectoryFact]
    public async Task A_person_is_found_again_by_objectGUID_with_the_account_state()
    {
        var mehmet = Assert.Single((await Search("mehmet")).People);
        var options = TestActiveDirectory.Options();
        using var factory = Factory(options);
        var directory = Directory(factory, options);

        var found = await directory.FindAsync(mehmet.ObjectGuid, CancellationToken.None);
        var missing = await directory.FindAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(DirectoryLookupStatus.Succeeded, found.Status);
        Assert.Equal(mehmet, found.Person);
        Assert.Equal(DirectoryLookupStatus.NotFound, missing.Status);
    }

    [ActiveDirectoryFact]
    public async Task A_disabled_account_found_by_objectGUID_says_it_is_disabled()
    {
        var options = TestActiveDirectory.Options();
        using var factory = Factory(options);
        using var connection = await factory.ConnectAsync(CancellationToken.None);
        await connection.BindAsync(options.ServiceAccountBindName, options.ServiceAccountPassword, CancellationToken.None);
        var entry = Assert.Single((await connection.SearchAsync(
            options.BaseDn, Novell.Directory.Ldap.LdapConnection.ScopeSub, "(sAMAccountName=disabled.user)", ["objectGUID"], CancellationToken.None)).Entries);

        var found = await Directory(factory, options).FindAsync(new Guid(entry.GetAttributeSet().Find("objectGUID")!.ByteValue), CancellationToken.None);

        Assert.Equal("disabled.user", found.Person!.SamAccountName);
        Assert.False(found.Person.IsEnabled);
    }

    [ActiveDirectoryFact]
    public async Task People_outside_the_employee_search_base_are_not_found()
    {
        var options = TestActiveDirectory.Options();
        options.EmployeeSearchBaseDn = "OU=Sahte," + options.BaseDn;
        using var factory = Factory(options);

        var result = await Directory(factory, options).SearchAsync("mehmet", 5, CancellationToken.None);

        Assert.Equal(DirectoryLookupStatus.Succeeded, result.Status);
        Assert.Empty(result.People);
    }

    [ActiveDirectoryFact]
    public async Task A_wrong_service_account_password_makes_the_directory_unavailable()
    {
        var options = TestActiveDirectory.Options();
        options.ServiceAccountPassword = "Wrong-Password-1";
        var logger = new CapturingLogger<LdapEmployeeDirectory>();
        using var factory = Factory(options);

        var result = await Directory(factory, options, logger).SearchAsync("mehmet", 5, CancellationToken.None);

        Assert.Equal(DirectoryLookupStatus.DirectoryUnavailable, result.Status);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("refused the service account", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("Wrong-Password-1", StringComparison.Ordinal));
    }

    private static async Task<DirectoryPeopleResult> Search(string term, int limit = 20)
    {
        var options = TestActiveDirectory.Options();
        using var factory = Factory(options);
        return await Directory(factory, options).SearchAsync(term, limit, CancellationToken.None);
    }

    private static LdapConnectionFactory Factory(ActiveDirectoryOptions options) =>
        new(Microsoft.Extensions.Options.Options.Create(options), NullLogger<LdapConnectionFactory>.Instance);

    private static LdapEmployeeDirectory Directory(
        LdapConnectionFactory factory, ActiveDirectoryOptions options, ILogger<LdapEmployeeDirectory>? logger = null) =>
        new(factory, Microsoft.Extensions.Options.Options.Create(options), logger ?? NullLogger<LdapEmployeeDirectory>.Instance);
}
