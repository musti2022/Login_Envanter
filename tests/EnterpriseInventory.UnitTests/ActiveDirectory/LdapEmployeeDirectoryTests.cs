using EnterpriseInventory.Application.Employees;
using EnterpriseInventory.Infrastructure.ActiveDirectory;
using Microsoft.Extensions.Logging.Abstractions;
using Novell.Directory.Ldap;

namespace EnterpriseInventory.UnitTests.ActiveDirectory;

/// <summary>
/// The LDAP side of the employee search against a scripted directory: the filter sent and how entries are read.
/// The real directory is exercised by SambaEmployeeDirectoryTests in the integration suite.
/// </summary>
public sealed class LdapEmployeeDirectoryTests : IDisposable
{
    private readonly ScriptedConnection _connection = new();
    private readonly ActiveDirectoryOptions _options = new()
    {
        Domain = "corp.example.com",
        BaseDn = "OU=BT,DC=corp,DC=example,DC=com",
        ServiceAccountUserName = "svc.envanter",
        ServiceAccountPassword = "service-password",
    };

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Each_word_must_start_a_name_of_an_enabled_person()
    {
        await Directory().SearchAsync("mehmet öz", 20, CancellationToken.None);

        Assert.Equal(
            "(&(objectCategory=person)(objectClass=user)(!(userAccountControl:1.2.840.113556.1.4.803:=2))"
            + "(|(sAMAccountName=mehmet*)(displayName=mehmet*)(givenName=mehmet*)(sn=mehmet*)(mail=mehmet*))"
            + "(|(sAMAccountName=öz*)(displayName=öz*)(givenName=öz*)(sn=öz*)(mail=öz*)))",
            _connection.LastFilter);
        Assert.Equal(("svc.envanter@corp.example.com", "service-password"), _connection.BoundAs);
        Assert.Equal(20, _connection.LastMaxResults);
    }

    [Fact]
    public async Task Wildcards_and_parentheses_typed_by_the_user_are_escaped()
    {
        await Directory().SearchAsync(@"*)(mail=* a\b", 20, CancellationToken.None);

        Assert.Contains(@"(sAMAccountName=\2a\29\28mail=\2a*)", _connection.LastFilter, StringComparison.Ordinal);
        Assert.Contains(@"(sn=a\5cb*)", _connection.LastFilter, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Employees_are_searched_under_the_employee_search_base_when_one_is_set()
    {
        await Directory().SearchAsync("mehmet", 20, CancellationToken.None);
        Assert.Equal(_options.BaseDn, _connection.LastSearchBase);

        _options.EmployeeSearchBaseDn = "DC=corp,DC=example,DC=com";
        await Directory().SearchAsync("mehmet", 20, CancellationToken.None);
        Assert.Equal("DC=corp,DC=example,DC=com", _connection.LastSearchBase);
    }

    [Fact]
    public async Task Entries_are_read_as_people_and_entries_without_identifiers_are_skipped()
    {
        var guid = Guid.NewGuid();
        _connection.Entries =
        [
            Entry(guid, "mehmet.user", ("displayName", "Mehmet Öztürk"), ("mail", "mehmet@corp.example.com"), ("department", " Muhasebe "), ("userAccountControl", "512")),
            new LdapEntry("CN=Bozuk,DC=corp,DC=example,DC=com", [new LdapAttribute("sAMAccountName", "bozuk")]),
        ];
        _connection.Truncated = true;

        var result = await Directory().SearchAsync("mehmet", 20, CancellationToken.None);

        Assert.Equal(DirectoryLookupStatus.Succeeded, result.Status);
        Assert.Equal(new DirectoryPerson(guid, "mehmet.user", "Mehmet Öztürk", "mehmet@corp.example.com", "Muhasebe", null, true), Assert.Single(result.People));
        Assert.True(result.HasMore);
    }

    [Theory]
    [InlineData("514", false)]
    [InlineData("66050", false)]
    [InlineData("66048", true)]
    [InlineData(null, false)]
    [InlineData("not-a-number", false)]
    public async Task An_account_is_enabled_only_when_its_flags_say_so(string? userAccountControl, bool enabled)
    {
        var guid = Guid.NewGuid();
        _connection.Entries = [userAccountControl is null ? Entry(guid, "kisi") : Entry(guid, "kisi", ("userAccountControl", userAccountControl))];

        var found = await Directory().FindAsync(guid, CancellationToken.None);

        Assert.Equal(enabled, found.Person!.IsEnabled);
        Assert.Equal("kisi", found.Person.DisplayName);
        Assert.Contains(DirectoryValues.EscapeFilterBytes(guid.ToByteArray()), _connection.LastFilter, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_accounts_with_one_objectGUID_make_the_lookup_fail_closed()
    {
        var guid = Guid.NewGuid();
        _connection.Entries = [Entry(guid, "bir"), Entry(guid, "iki")];

        Assert.Equal(DirectoryLookupStatus.DirectoryUnavailable, (await Directory().FindAsync(guid, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task A_refused_service_account_or_an_unreachable_server_makes_the_directory_unavailable()
    {
        _connection.BindError = new LdapException("refused", LdapException.InvalidCredentials, "80090308: LdapErr: DSID-0C09044E, data 52e");
        Assert.Equal(DirectoryLookupStatus.DirectoryUnavailable, (await Directory().SearchAsync("mehmet", 20, CancellationToken.None)).Status);

        var unreachable = new LdapEmployeeDirectory(
            new UnreachableFactory(), Microsoft.Extensions.Options.Options.Create(_options), NullLogger<LdapEmployeeDirectory>.Instance);
        Assert.Equal(DirectoryLookupStatus.DirectoryUnavailable, (await unreachable.FindAsync(Guid.NewGuid(), CancellationToken.None)).Status);
    }

    private LdapEmployeeDirectory Directory() =>
        new(new ScriptedFactory(_connection), Microsoft.Extensions.Options.Options.Create(_options), NullLogger<LdapEmployeeDirectory>.Instance);

    private static LdapEntry Entry(Guid guid, string sam, params (string Name, string Value)[] values)
    {
        var attributes = new LdapAttributeSet
        {
            new LdapAttribute("objectGUID", guid.ToByteArray()),
            new LdapAttribute("sAMAccountName", sam),
        };
        foreach (var (name, value) in values)
        {
            attributes.Add(new LdapAttribute(name, value));
        }

        return new LdapEntry($"CN={sam},DC=corp,DC=example,DC=com", attributes);
    }

    private sealed class ScriptedFactory(ScriptedConnection connection) : ILdapConnectionFactory
    {
        public Task<IDirectoryConnection> ConnectAsync(CancellationToken cancellationToken) => Task.FromResult<IDirectoryConnection>(connection);
    }

    private sealed class UnreachableFactory : ILdapConnectionFactory
    {
        public Task<IDirectoryConnection> ConnectAsync(CancellationToken cancellationToken) =>
            throw new DirectoryUnavailableException(DirectoryFailure.Unreachable, "dc1.corp.example.com:636 is unreachable.");
    }

    private sealed class ScriptedConnection : IDirectoryConnection
    {
        public IReadOnlyList<LdapEntry> Entries { get; set; } = [];

        public bool Truncated { get; set; }

        public LdapException? BindError { get; set; }

        public (string Name, string Password)? BoundAs { get; private set; }

        public string? LastFilter { get; private set; }

        public string? LastSearchBase { get; private set; }

        public int LastMaxResults { get; private set; }

        public Task BindAsync(string name, string password, CancellationToken cancellationToken)
        {
            BoundAs = (name, password);
            return BindError is null ? Task.CompletedTask : Task.FromException(BindError);
        }

        public Task<string?> WhoAmIAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task<DirectorySearchResult> SearchAsync(
            string searchBase, int scope, string filter, IReadOnlyCollection<string> attributes, CancellationToken cancellationToken)
        {
            (LastSearchBase, LastFilter) = (searchBase, filter);
            return Task.FromResult(new DirectorySearchResult(Entries, 0));
        }

        public Task<DirectorySearchResult> SearchManyAsync(
            string searchBase, string filter, IReadOnlyCollection<string> attributes, int maxResults, CancellationToken cancellationToken)
        {
            (LastSearchBase, LastFilter, LastMaxResults) = (searchBase, filter, maxResults);
            return Task.FromResult(new DirectorySearchResult(Entries, 0, Truncated));
        }

        public void Dispose()
        {
        }
    }
}
