using EnterpriseInventory.Infrastructure.ActiveDirectory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Novell.Directory.Ldap;

namespace EnterpriseInventory.IntegrationTests.ActiveDirectory;

/// <summary>
/// LDAPS against a real directory server (the Samba AD test domain; see scripts/test-ad). Skipped when the
/// EI_TEST_AD_* variables are not set.
/// </summary>
public sealed class SambaLdapsTests
{
    [ActiveDirectoryFact]
    public async Task Ldaps_connection_to_the_test_domain_controller_is_trusted_and_answers()
    {
        var options = TestActiveDirectory.Options();
        using var factory = Factory(options);

        using var connection = await factory.ConnectAsync(CancellationToken.None);
        var rootDse = await connection.SearchAsync(string.Empty, LdapConnection.ScopeBase, "(objectClass=*)", ["defaultNamingContext"], CancellationToken.None);

        var entry = Assert.Single(rootDse.Entries);
        Assert.Equal(options.BaseDn, entry.GetAttributeSet().Find("defaultNamingContext")?.StringValue, ignoreCase: true);
    }

    [ActiveDirectoryFact]
    public async Task Connecting_by_a_name_the_certificate_does_not_carry_is_refused()
    {
        var options = TestActiveDirectory.Options();
        // The address the domain controller listens on, which its certificate (issued for dc1.envanter.test) does not carry.
        options.ServerFqdn = "127.0.0.1";
        using var factory = Factory(options);

        var error = await Assert.ThrowsAsync<DirectoryUnavailableException>(() => factory.ConnectAsync(CancellationToken.None));

        Assert.Equal(DirectoryFailure.CertificateRejected, error.Failure);
        Assert.Contains("not issued for the configured server name", error.Message, StringComparison.Ordinal);
    }

    [ActiveDirectoryFact]
    public async Task Without_the_test_ca_the_domain_controller_is_not_trusted()
    {
        var options = TestActiveDirectory.Options();
        options.TrustedCaCertificatePath = null;
        using var factory = Factory(options);

        var error = await Assert.ThrowsAsync<DirectoryUnavailableException>(() => factory.ConnectAsync(CancellationToken.None));

        Assert.Equal(DirectoryFailure.CertificateRejected, error.Failure);
    }

    [ActiveDirectoryFact]
    public async Task Health_check_reports_the_directory_healthy()
    {
        var options = TestActiveDirectory.Options();
        using var factory = Factory(options);

        var result = await HealthCheck(factory, options);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [ActiveDirectoryFact]
    public async Task Health_check_reports_a_base_dn_outside_the_directory()
    {
        var options = TestActiveDirectory.Options();
        options.BaseDn = "DC=other,DC=test";
        using var factory = Factory(options);

        var result = await HealthCheck(factory, options);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("is not inside the directory's domain", result.Description, StringComparison.Ordinal);
    }

    private static LdapConnectionFactory Factory(ActiveDirectoryOptions options) =>
        new(Microsoft.Extensions.Options.Options.Create(options), NullLogger<LdapConnectionFactory>.Instance);

    private static Task<HealthCheckResult> HealthCheck(LdapConnectionFactory factory, ActiveDirectoryOptions options)
    {
        var check = new ActiveDirectoryHealthCheck(factory, Microsoft.Extensions.Options.Options.Create(options));
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("active-directory", check, HealthStatus.Degraded, null),
        };
        return check.CheckHealthAsync(context);
    }
}
