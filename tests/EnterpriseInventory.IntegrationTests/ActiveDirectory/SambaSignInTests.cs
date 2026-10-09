using EnterpriseInventory.Application.Authentication;
using EnterpriseInventory.Infrastructure.ActiveDirectory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Novell.Directory.Ldap;

namespace EnterpriseInventory.IntegrationTests.ActiveDirectory;

/// <summary>
/// Sign-in against the Samba AD test domain (see scripts/test-ad for its users and groups): passwords are checked
/// by the directory (day 7) and only members of the group with the configured SID get in (day 8).
/// </summary>
public sealed class SambaSignInTests : IDisposable
{
    private readonly List<CountingConnectionFactory> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    // --- Day 7: the directory decides whether the password is right -------------------------------------------

    [ActiveDirectoryFact]
    public async Task Right_password_of_a_member_signs_in_with_the_directory_identity()
    {
        var result = await SignIn("ayse.admin");

        Assert.Equal(DirectorySignInStatus.Succeeded, result.Status);
        var account = result.Account!;
        Assert.Equal("ayse.admin", account.SamAccountName);
        Assert.Equal("Ayşe Yılmaz", account.DisplayName);
        Assert.NotEqual(Guid.Empty, account.ObjectGuid);
        Assert.StartsWith(DirectoryValues.SplitRid(TestActiveDirectory.GroupSid).DomainSid + "-", account.SecurityIdentifier, StringComparison.Ordinal);
    }

    [ActiveDirectoryFact]
    public async Task The_same_user_always_gets_the_same_identity()
    {
        var first = await SignIn("ayse.admin");
        var second = await SignIn("AYSE.ADMIN@ENVANTER.TEST");

        Assert.Equal(DirectorySignInStatus.Succeeded, second.Status);
        Assert.Equal(first.Account!.ObjectGuid, second.Account!.ObjectGuid);
        Assert.Equal(first.Account.SecurityIdentifier, second.Account.SecurityIdentifier);
    }

    [ActiveDirectoryTheory]
    [InlineData("ayse.admin", "Wrong-Password-1")]
    [InlineData("ayse.admin", "wrong")]
    [InlineData("nobody.here", null)]
    [InlineData("mehmet.user", "Wrong-Password-1")]
    public async Task Wrong_password_or_unknown_user_is_refused(string userName, string? password)
    {
        var result = await SignIn(userName, password ?? TestActiveDirectory.UserPassword);

        Assert.Equal(DirectorySignInStatus.InvalidCredentials, result.Status);
        Assert.Null(result.Account);
    }

    [ActiveDirectoryTheory]
    [InlineData("disabled.user", DirectorySignInStatus.AccountDisabled)]
    [InlineData("expired.user", DirectorySignInStatus.AccountExpired)]
    [InlineData("mustchange.user", DirectorySignInStatus.PasswordMustChange)]
    public async Task Accounts_that_cannot_sign_in_are_refused_even_with_the_right_password(string userName, DirectorySignInStatus expected)
    {
        var result = await SignIn(userName);

        Assert.Equal(expected, result.Status);
        Assert.Null(result.Account);
    }

    [ActiveDirectoryTheory]
    [InlineData("disabled.user")]
    [InlineData("expired.user")]
    public async Task Account_state_is_not_revealed_without_the_right_password(string userName)
    {
        var result = await SignIn(userName, "Wrong-Password-1");

        Assert.Equal(DirectorySignInStatus.InvalidCredentials, result.Status);
    }

    [ActiveDirectoryTheory]
    [InlineData("ayse.admin", "")]
    [InlineData("ayse.admin@other.test", null)]
    [InlineData("*", null)]
    [InlineData("ayse.admin)(objectClass=*", null)]
    [InlineData("ENVANTER\\ayse.admin", null)]
    public async Task Unusable_input_is_refused_without_contacting_the_directory(string userName, string? password)
    {
        var connections = Connections(TestActiveDirectory.Options());

        var result = await Service(connections).SignInAsync(userName, password ?? TestActiveDirectory.UserPassword, CancellationToken.None);

        Assert.Equal(DirectorySignInStatus.InvalidCredentials, result.Status);
        Assert.Equal(0, connections.Count);
    }

    [Fact]
    public async Task Unreachable_directory_fails_closed()
    {
        var options = new ActiveDirectoryOptions
        {
            Domain = "unreachable.invalid",
            ServerFqdn = "dc1.unreachable.invalid",
            BaseDn = "DC=unreachable,DC=invalid",
            AllowedGroupSid = "S-1-5-21-1-2-3-1105",
            NestedGroupPolicy = NestedGroupPolicy.DirectMembershipOnly,
            ConnectTimeoutSeconds = 2,
        };
        var logger = new CapturingLogger<LdapDirectoryService>();

        var result = await Service(Connections(options), logger).SignInAsync("ayse.admin", "Any-Password-1", CancellationToken.None);

        Assert.Equal(DirectorySignInStatus.DirectoryUnavailable, result.Status);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("cannot be reached", StringComparison.Ordinal));
    }

    // --- Day 8: membership of the group with the configured SID ---------------------------------------------

    [ActiveDirectoryTheory]
    [InlineData("mehmet.user", NestedGroupPolicy.DirectMembershipOnly)]
    [InlineData("mehmet.user", NestedGroupPolicy.IncludeNested)]
    [InlineData("decoy.user", NestedGroupPolicy.DirectMembershipOnly)]
    [InlineData("decoy.user", NestedGroupPolicy.IncludeNested)]
    [InlineData("nested.user", NestedGroupPolicy.DirectMembershipOnly)]
    public async Task Users_outside_the_group_are_refused_after_proving_their_password(string userName, NestedGroupPolicy policy)
    {
        var result = await SignIn(userName, policy: policy);

        Assert.Equal(DirectorySignInStatus.NotAuthorized, result.Status);
        Assert.Null(result.Account);
    }

    [ActiveDirectoryTheory]
    [InlineData("ayse.admin", NestedGroupPolicy.DirectMembershipOnly)]
    [InlineData("ayse.admin", NestedGroupPolicy.IncludeNested)]
    [InlineData("primary.user", NestedGroupPolicy.DirectMembershipOnly)]
    [InlineData("primary.user", NestedGroupPolicy.IncludeNested)]
    [InlineData("nested.user", NestedGroupPolicy.IncludeNested)]
    public async Task Members_get_in_according_to_the_nested_group_policy(string userName, NestedGroupPolicy policy)
    {
        var result = await SignIn(userName, policy: policy);

        Assert.Equal(DirectorySignInStatus.Succeeded, result.Status);
    }

    [ActiveDirectoryTheory]
    [InlineData(NestedGroupPolicy.DirectMembershipOnly)]
    [InlineData(NestedGroupPolicy.IncludeNested)]
    public async Task Access_follows_the_configured_sid_not_the_group_name(NestedGroupPolicy policy)
    {
        // Point the configuration at the decoy group, which has the same name in another OU.
        var options = TestActiveDirectory.Options(policy);
        options.AllowedGroupSid = TestActiveDirectory.DecoyGroupSid;
        var service = Service(Connections(options));

        var decoy = await service.SignInAsync("decoy.user", TestActiveDirectory.UserPassword, CancellationToken.None);
        var ayse = await service.SignInAsync("ayse.admin", TestActiveDirectory.UserPassword, CancellationToken.None);

        Assert.Equal(DirectorySignInStatus.Succeeded, decoy.Status);
        Assert.Equal(DirectorySignInStatus.NotAuthorized, ayse.Status);
    }

    [ActiveDirectoryFact]
    public async Task Members_outside_base_dn_are_refused()
    {
        var options = TestActiveDirectory.Options();
        options.BaseDn = "OU=Sahte," + options.BaseDn;

        var result = await Service(Connections(options)).SignInAsync("ayse.admin", TestActiveDirectory.UserPassword, CancellationToken.None);

        Assert.Equal(DirectorySignInStatus.NotAuthorized, result.Status);
    }

    [ActiveDirectoryFact]
    public async Task A_group_sid_that_does_not_exist_lets_nobody_in()
    {
        var options = TestActiveDirectory.Options();
        options.AllowedGroupSid = DirectoryValues.SplitRid(TestActiveDirectory.GroupSid).DomainSid + "-999999";
        var logger = new CapturingLogger<LdapDirectoryService>();

        var result = await Service(Connections(options), logger).SignInAsync("ayse.admin", TestActiveDirectory.UserPassword, CancellationToken.None);

        Assert.Equal(DirectorySignInStatus.NotAuthorized, result.Status);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("No group with the configured AllowedGroupSid", StringComparison.Ordinal));
    }

    private Task<DirectorySignInResult> SignIn(
        string userName, string? password = null, NestedGroupPolicy policy = NestedGroupPolicy.DirectMembershipOnly) =>
        Service(Connections(TestActiveDirectory.Options(policy)))
            .SignInAsync(userName, password ?? TestActiveDirectory.UserPassword, CancellationToken.None);

    private CountingConnectionFactory Connections(ActiveDirectoryOptions options)
    {
        var factory = new CountingConnectionFactory(options);
        _factories.Add(factory);
        return factory;
    }

    private static LdapDirectoryService Service(CountingConnectionFactory connections, ILogger<LdapDirectoryService>? logger = null) =>
        new(connections, Microsoft.Extensions.Options.Options.Create(connections.Options), logger ?? NullLogger<LdapDirectoryService>.Instance);

    /// <summary>The real connection factory, counting how often the directory is contacted.</summary>
    private sealed class CountingConnectionFactory(ActiveDirectoryOptions options) : ILdapConnectionFactory, IDisposable
    {
        private readonly LdapConnectionFactory _inner = new(
            Microsoft.Extensions.Options.Options.Create(options), NullLogger<LdapConnectionFactory>.Instance);

        public ActiveDirectoryOptions Options => options;

        public int Count { get; private set; }

        public Task<ILdapConnection> ConnectAsync(CancellationToken cancellationToken)
        {
            Count++;
            return _inner.ConnectAsync(cancellationToken);
        }

        public void Dispose() => _inner.Dispose();
    }
}
