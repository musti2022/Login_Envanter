using EnterpriseInventory.Infrastructure.ActiveDirectory;
using Microsoft.Extensions.Logging;

namespace EnterpriseInventory.IntegrationTests.ActiveDirectory;

/// <summary>
/// The test directory built by <c>scripts/test-ad/setup-samba-ad.sh</c> (or any AD prepared the same way),
/// described by the <c>EI_TEST_AD_*</c> environment variables that script writes. Tests that need it are
/// skipped when <c>EI_TEST_AD_SERVER</c> is not set.
/// </summary>
internal static class TestActiveDirectory
{
    public static string? Server => Environment.GetEnvironmentVariable("EI_TEST_AD_SERVER");

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(Server);

    public static string UserPassword => Required("EI_TEST_AD_USER_PASSWORD");

    public static string GroupSid => Required("EI_TEST_AD_GROUP_SID");

    public static string DecoyGroupSid => Required("EI_TEST_AD_DECOY_GROUP_SID");

    public static string ServiceUser => Required("EI_TEST_AD_SERVICE_USER");

    public static string ServicePassword => Required("EI_TEST_AD_SERVICE_PASSWORD");

    public static ActiveDirectoryOptions Options(NestedGroupPolicy nestedGroupPolicy = NestedGroupPolicy.DirectMembershipOnly) => new()
    {
        Mode = DirectoryMode.Ldap,
        Domain = Required("EI_TEST_AD_DOMAIN"),
        ServerFqdn = Required("EI_TEST_AD_SERVER"),
        Port = int.Parse(Required("EI_TEST_AD_PORT"), System.Globalization.CultureInfo.InvariantCulture),
        UseLdaps = true,
        BaseDn = Required("EI_TEST_AD_BASE_DN"),
        AllowedGroupSid = GroupSid,
        NestedGroupPolicy = nestedGroupPolicy,
        ServiceAccountUserName = ServiceUser,
        ServiceAccountPassword = ServicePassword,
        TrustedCaCertificatePath = Required("EI_TEST_AD_CA_CERT"),

        // The test CA publishes no revocation list.
        CheckCertificateRevocation = false,
        ConnectTimeoutSeconds = 5,
        OperationTimeoutSeconds = 10,
    };

    /// <summary>The same directory as configuration keys, for hosting the API against it.</summary>
    public static Dictionary<string, string?> Settings(NestedGroupPolicy nestedGroupPolicy = NestedGroupPolicy.DirectMembershipOnly)
    {
        var options = Options(nestedGroupPolicy);
        return new Dictionary<string, string?>
        {
            ["ActiveDirectory:Mode"] = options.Mode.ToString(),
            ["ActiveDirectory:Domain"] = options.Domain,
            ["ActiveDirectory:ServerFqdn"] = options.ServerFqdn,
            ["ActiveDirectory:Port"] = options.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["ActiveDirectory:BaseDn"] = options.BaseDn,
            ["ActiveDirectory:AllowedGroupSid"] = options.AllowedGroupSid,
            ["ActiveDirectory:NestedGroupPolicy"] = options.NestedGroupPolicy.ToString(),
            ["ActiveDirectory:ServiceAccountUserName"] = options.ServiceAccountUserName,
            ["ActiveDirectory:ServiceAccountPassword"] = options.ServiceAccountPassword,
            ["ActiveDirectory:TrustedCaCertificatePath"] = options.TrustedCaCertificatePath,
            ["ActiveDirectory:CheckCertificateRevocation"] = "false",
        };
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} is not set; run scripts/test-ad/setup-samba-ad.sh and source its env file.");
}

/// <summary>A fact that runs only when the test directory is configured (see <see cref="TestActiveDirectory"/>).</summary>
public sealed class ActiveDirectoryFactAttribute : FactAttribute
{
    public ActiveDirectoryFactAttribute()
    {
        if (!TestActiveDirectory.IsConfigured)
        {
            Skip = "EI_TEST_AD_SERVER is not set; see scripts/test-ad/README.md.";
        }
    }
}

/// <summary>A theory that runs only when the test directory is configured.</summary>
public sealed class ActiveDirectoryTheoryAttribute : TheoryAttribute
{
    public ActiveDirectoryTheoryAttribute()
    {
        if (!TestActiveDirectory.IsConfigured)
        {
            Skip = "EI_TEST_AD_SERVER is not set; see scripts/test-ad/README.md.";
        }
    }
}

/// <summary>Keeps formatted log messages so tests can assert what was (and was not) logged.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_entries)
        {
            _entries.Add((logLevel, formatter(state, exception)));
        }
    }
}

/// <summary>A fact that needs both the test directory and the test SQL Server.</summary>
public sealed class ActiveDirectoryAndSqlServerFactAttribute : FactAttribute
{
    public ActiveDirectoryAndSqlServerFactAttribute()
    {
        if (!TestActiveDirectory.IsConfigured)
        {
            Skip = "EI_TEST_AD_SERVER is not set; see scripts/test-ad/README.md.";
        }
        else if (Persistence.SqlServerDatabaseFixture.ServerConnectionString is null)
        {
            Skip = $"{Persistence.SqlServerDatabaseFixture.ConnectionVariable} is not set; SQL Server tests are skipped.";
        }
    }
}
