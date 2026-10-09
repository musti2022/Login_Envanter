using EnterpriseInventory.Application.Authentication;
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

    private static FakeDirectoryService Service() => new(Options(), new TestHostEnvironment("Development"));

    private static IOptions<ActiveDirectoryOptions> Options()
    {
        var options = new ActiveDirectoryOptions { Mode = DirectoryMode.Fake };
        options.FakeUsers.Add(new FakeDirectoryUser { UserName = "dev.admin", Password = Password, DisplayName = "Geliştirici Yönetici" });
        options.FakeUsers.Add(new FakeDirectoryUser { UserName = "dev.user", Password = Password, IsAllowedGroupMember = false });
        options.FakeUsers.Add(new FakeDirectoryUser { UserName = "dev.disabled", Password = Password, IsDisabled = true });
        return Microsoft.Extensions.Options.Options.Create(options);
    }
}
