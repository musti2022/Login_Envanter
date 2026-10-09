using System.Diagnostics;
using EnterpriseInventory.Application.Authentication;
using EnterpriseInventory.Infrastructure.ActiveDirectory;
using Microsoft.Extensions.Logging;
using Novell.Directory.Ldap;

namespace EnterpriseInventory.UnitTests.ActiveDirectory;

/// <summary>
/// The sign-in logic against a scripted directory, for answers a real domain controller rarely gives. The real
/// directory is exercised by the Samba tests in the integration suite.
/// </summary>
public sealed class LdapDirectoryServiceTests : IDisposable
{
    private const string Password = "Correct-Horse-Battery-Staple-42";
    private const string GroupSid = "S-1-5-21-1-2-3-1105";
    private const string UserDn = "CN=Ayşe Yılmaz,OU=Users,DC=corp,DC=example,DC=com";

    private readonly ScriptedConnection _connection = new();
    private readonly ListLogger<LdapDirectoryService> _logger = new();

    public LdapDirectoryServiceTests()
    {
        _connection.WhoAmI = @"u:CORP\ayse.admin";
        _connection.Users.Add(User());
        _connection.TokenGroups = [GroupSid];
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task A_member_whose_bind_opened_their_own_account_signs_in()
    {
        var result = await SignIn();

        Assert.Equal(DirectorySignInStatus.Succeeded, result.Status);
        Assert.Equal("ayse.admin", result.Account!.SamAccountName);
        Assert.Equal(("ayse.admin@corp.example.com", Password), _connection.BoundAs);
        Assert.True(_connection.IsDisposed);
    }

    [Theory]
    [InlineData(@"u:CORP\clash.other")]
    [InlineData("dn:CN=Clash Other,OU=Users,DC=corp,DC=example,DC=com")]
    public async Task A_password_that_opened_another_account_is_refused(string boundAs)
    {
        _connection.WhoAmI = boundAs;

        var result = await SignIn();

        Assert.Equal(DirectorySignInStatus.InvalidCredentials, result.Status);
        Assert.Contains(_logger.Messages, m => m.Level == LogLevel.Warning && m.Text.Contains("opened the account", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_dn_answer_naming_the_account_itself_is_accepted()
    {
        _connection.WhoAmI = "dn:" + UserDn.ToUpperInvariant();

        var result = await SignIn();

        Assert.Equal(DirectorySignInStatus.Succeeded, result.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("u:ayse.admin@corp.example.com")]
    [InlineData(@"u:CORP\")]
    [InlineData("anonymous")]
    public async Task An_unclear_who_am_i_answer_fails_closed(string? boundAs)
    {
        _connection.WhoAmI = boundAs;

        var result = await SignIn();

        Assert.Equal(DirectorySignInStatus.DirectoryUnavailable, result.Status);
    }

    [Fact]
    public async Task An_account_without_user_account_control_fails_closed()
    {
        _connection.Users[0] = User(userAccountControl: null);

        var result = await SignIn();

        Assert.Equal(DirectorySignInStatus.DirectoryUnavailable, result.Status);
        Assert.Contains(_logger.Messages, m => m.Text.Contains("no readable userAccountControl", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_disabled_account_is_refused()
    {
        _connection.Users[0] = User(userAccountControl: 0x202);

        var result = await SignIn();

        Assert.Equal(DirectorySignInStatus.AccountDisabled, result.Status);
    }

    [Fact]
    public async Task Referrals_not_followed_are_named_when_the_user_is_not_found()
    {
        _connection.Users.Clear();
        _connection.SkippedReferrals = 3;

        var result = await SignIn();

        Assert.Equal(DirectorySignInStatus.NotAuthorized, result.Status);
        Assert.Contains(_logger.Messages, m => m.Text.Contains("(3 referrals not followed)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_lockout_reported_by_the_bind_is_passed_on()
    {
        _connection.BindError = new LdapException(
            "Invalid Credentials", LdapException.InvalidCredentials, "80090308: LdapErr: DSID-0C09044E, comment: AcceptSecurityContext error, data 775, v4563");

        var result = await SignIn();

        Assert.Equal(DirectorySignInStatus.AccountLocked, result.Status);
    }

    [Fact]
    public async Task A_directory_that_stops_answering_is_given_up_on_at_the_deadline()
    {
        _connection.HangOnBind = true;
        var stopwatch = Stopwatch.StartNew();

        var result = await SignIn();

        Assert.Equal(DirectorySignInStatus.DirectoryUnavailable, result.Status);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromSeconds(1.9), TimeSpan.FromSeconds(10));
        Assert.Contains(_logger.Messages, m => m.Text.Contains("did not finish within 2 seconds", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_callers_cancellation_is_not_turned_into_a_refusal()
    {
        _connection.HangOnBind = true;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().SignInAsync("ayse.admin", Password, cancellation.Token));
    }

    private Task<DirectorySignInResult> SignIn() => Service().SignInAsync("ayse.admin", Password, CancellationToken.None);

    private LdapDirectoryService Service() => new(
        new ScriptedFactory(_connection),
        Microsoft.Extensions.Options.Options.Create(new ActiveDirectoryOptions
        {
            Domain = "corp.example.com",
            ServerFqdn = "dc01.corp.example.com",
            BaseDn = "DC=corp,DC=example,DC=com",
            AllowedGroupSid = GroupSid,
            NestedGroupPolicy = NestedGroupPolicy.IncludeNested,
            ConnectTimeoutSeconds = 1,
            OperationTimeoutSeconds = 1,
        }),
        _logger);

    private static LdapEntry User(int? userAccountControl = 0x200)
    {
        var attributes = new LdapAttributeSet
        {
            new LdapAttribute("objectGUID", Guid.NewGuid().ToByteArray()),
            new LdapAttribute("objectSid", DirectoryValues.SidToBytes("S-1-5-21-1-2-3-1201")),
            new LdapAttribute("sAMAccountName", "ayse.admin"),
            new LdapAttribute("displayName", "Ayşe Yılmaz"),
        };
        if (userAccountControl is { } flags)
        {
            attributes.Add(new LdapAttribute("userAccountControl", flags.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return new LdapEntry(UserDn, attributes);
    }

    private sealed class ScriptedFactory(ScriptedConnection connection) : ILdapConnectionFactory
    {
        public Task<IDirectoryConnection> ConnectAsync(CancellationToken cancellationToken) => Task.FromResult<IDirectoryConnection>(connection);
    }

    /// <summary>A directory with one user, answering searches for the user and the user's tokenGroups.</summary>
    private sealed class ScriptedConnection : IDirectoryConnection
    {
        public string? WhoAmI { get; set; }

        public List<LdapEntry> Users { get; } = [];

        public IReadOnlyList<string> TokenGroups { get; set; } = [];

        public int SkippedReferrals { get; set; }

        public LdapException? BindError { get; set; }

        public bool HangOnBind { get; set; }

        public (string Name, string Password)? BoundAs { get; private set; }

        public bool IsDisposed { get; private set; }

        public async Task BindAsync(string name, string password, CancellationToken cancellationToken)
        {
            if (HangOnBind)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (BindError is not null)
            {
                throw BindError;
            }

            BoundAs = (name, password);
        }

        public Task<string?> WhoAmIAsync(CancellationToken cancellationToken) => Task.FromResult(WhoAmI);

        public Task<DirectorySearchResult> SearchAsync(
            string searchBase, int scope, string filter, IReadOnlyCollection<string> attributes, CancellationToken cancellationToken)
        {
            if (scope == LdapConnection.ScopeBase)
            {
                var groups = new LdapAttribute("tokenGroups");
                foreach (var sid in TokenGroups)
                {
                    groups.AddValue(DirectoryValues.SidToBytes(sid));
                }

                return Task.FromResult(new DirectorySearchResult([new LdapEntry(searchBase, [groups])], 0));
            }

            return Task.FromResult(new DirectorySearchResult([.. Users], SkippedReferrals));
        }

        public void Dispose() => IsDisposed = true;
    }
}

internal sealed class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Text)> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Messages)
        {
            Messages.Add((logLevel, formatter(state, exception)));
        }
    }
}
