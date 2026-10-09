using EnterpriseInventory.Application.Authentication;
using EnterpriseInventory.Application.Employees;
using EnterpriseInventory.Infrastructure.ActiveDirectory;
using Microsoft.Extensions.Options;

namespace EnterpriseInventory.UnitTests.ActiveDirectory;

public class FakeDirectoryServiceTests
{
    private const string Password = "dev-only-password";

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Refuses_to_exist_outside_development(string environment)
    {
        Assert.Throws<InvalidOperationException>(() => new FakeDirectoryService(Options(), new TestHostEnvironment(environment)));
    }

    [Fact]
    public async Task Configured_member_with_the_right_password_signs_in()
    {
        var result = await Service().SignInAsync("dev.admin", Password, CancellationToken.None);

        Assert.Equal(DirectorySignInStatus.Succeeded, result.Status);
        Assert.Equal("dev.admin", result.Account!.SamAccountName);
        Assert.Equal("Geliştirici Yönetici", result.Account.DisplayName);
    }

    [Fact]
    public async Task Identifiers_are_stable_for_the_same_user()
    {
        var first = await Service().SignInAsync("dev.admin", Password, CancellationToken.None);
        var second = await Service().SignInAsync("DEV.ADMIN", Password, CancellationToken.None);

        Assert.Equal(first.Account!.ObjectGuid, second.Account!.ObjectGuid);
        Assert.Equal(first.Account.SecurityIdentifier, second.Account.SecurityIdentifier);
        Assert.NotEqual(Guid.Empty, first.Account.ObjectGuid);
    }

    [Theory]
    [InlineData("dev.admin", "wrong")]
    [InlineData("dev.admin", "")]
    [InlineData("nobody", Password)]
    [InlineData("dev.admin*", Password)]
    public async Task Wrong_password_or_unknown_user_is_refused(string userName, string password)
    {
        var result = await Service().SignInAsync(userName, password, CancellationToken.None);

        Assert.Equal(DirectorySignInStatus.InvalidCredentials, result.Status);
        Assert.Null(result.Account);
    }

    [Fact]
    public async Task Non_member_and_disabled_users_are_refused()
    {
        Assert.Equal(DirectorySignInStatus.NotAuthorized, (await Service().SignInAsync("dev.user", Password, CancellationToken.None)).Status);
        Assert.Equal(DirectorySignInStatus.AccountDisabled, (await Service().SignInAsync("dev.disabled", Password, CancellationToken.None)).Status);
    }

    [Theory]
    [InlineData("dev.admin", DirectoryAccessStatus.Allowed)]
    [InlineData("dev.user", DirectoryAccessStatus.NotAuthorized)]
    [InlineData("dev.disabled", DirectoryAccessStatus.AccountDisabled)]
    public async Task Access_checks_follow_the_configured_users(string userName, DirectoryAccessStatus expected)
    {
        // The identity the user would get by signing in, found through a member with the same settings.
        var options = Options();
        var member = options.Value.FakeUsers.Single(u => u.UserName == userName);
        member.IsAllowedGroupMember = true;
        member.IsDisabled = false;
        var objectGuid = (await new FakeDirectoryService(options, new TestHostEnvironment("Development")).SignInAsync(userName, Password, CancellationToken.None)).Account!.ObjectGuid;

        var status = await Service().CheckAccessAsync(objectGuid, CancellationToken.None);

        Assert.Equal(expected, status);
    }

    [Fact]
    public async Task An_unknown_user_is_not_found()
    {
        Assert.Equal(DirectoryAccessStatus.AccountNotFound, await Service().CheckAccessAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Theory]
    [InlineData("dev.u")]
    [InlineData("işık")]
    [InlineData("IŞIK")]
    [InlineData("isik")]
    [InlineData("deniz ış")]
    [InlineData("deniz@")]
    public async Task Employees_are_found_by_the_start_of_their_names_whether_or_not_they_may_sign_in(string term)
    {
        var result = await Service().SearchAsync(term, 10, CancellationToken.None);

        var person = Assert.Single(result.People);
        Assert.Equal("dev.user", person.SamAccountName);
        Assert.Equal("Bilgi İşlem", person.Department);
        Assert.True(person.IsEnabled);
    }

    [Fact]
    public async Task Disabled_users_are_not_found_and_more_matches_than_the_limit_are_reported()
    {
        var all = await Service().SearchAsync("dev", 10, CancellationToken.None);
        var limited = await Service().SearchAsync("dev", 1, CancellationToken.None);

        Assert.Equal(["dev.admin", "dev.user"], all.People.Select(p => p.SamAccountName));
        Assert.False(all.HasMore);
        Assert.Single(limited.People);
        Assert.True(limited.HasMore);
    }

    [Fact]
    public async Task An_employee_is_found_again_by_the_identity_a_search_gave()
    {
        var service = Service();
        var found = Assert.Single((await service.SearchAsync("deniz", 10, CancellationToken.None)).People);

        Assert.Equal(found, (await service.FindAsync(found.ObjectGuid, CancellationToken.None)).Person);
        Assert.Equal(DirectoryLookupStatus.NotFound, (await service.FindAsync(Guid.NewGuid(), CancellationToken.None)).Status);
    }

    private static FakeDirectoryService Service() => new(Options(), new TestHostEnvironment("Development"));

    private static IOptions<ActiveDirectoryOptions> Options()
    {
        var options = new ActiveDirectoryOptions { Mode = DirectoryMode.Fake };
        options.FakeUsers.Add(new FakeDirectoryUser { UserName = "dev.admin", Password = Password, DisplayName = "Geliştirici Yönetici" });
        options.FakeUsers.Add(new FakeDirectoryUser
        {
            UserName = "dev.user",
            Password = Password,
            DisplayName = "Deniz Işık",
            Email = "deniz@example.invalid",
            Department = "Bilgi İşlem",
            IsAllowedGroupMember = false,
        });
        options.FakeUsers.Add(new FakeDirectoryUser { UserName = "dev.disabled", Password = Password, IsDisabled = true });
        return Microsoft.Extensions.Options.Options.Create(options);
    }
}
