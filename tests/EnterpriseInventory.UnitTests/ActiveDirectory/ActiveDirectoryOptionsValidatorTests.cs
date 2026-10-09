using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EnterpriseInventory.Infrastructure.ActiveDirectory;

namespace EnterpriseInventory.UnitTests.ActiveDirectory;

public sealed class ActiveDirectoryOptionsValidatorTests : IDisposable
{
    private readonly string _tempDirectory = Directory.CreateTempSubdirectory("ei-ad-options-").FullName;

    public void Dispose() => Directory.Delete(_tempDirectory, recursive: true);

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Development")]
    public void Complete_ldaps_settings_are_accepted(string environment)
    {
        var result = Validate(ValidOptions(), environment);

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact]
    public void Plain_ldap_is_refused()
    {
        var options = ValidOptions();
        options.UseLdaps = false;

        AssertFails(options, "UseLdaps must be true");
    }

    [Theory]
    [InlineData(nameof(ActiveDirectoryOptions.Domain), "ActiveDirectory:Domain is required")]
    [InlineData(nameof(ActiveDirectoryOptions.ServerFqdn), "ActiveDirectory:ServerFqdn is required")]
    [InlineData(nameof(ActiveDirectoryOptions.BaseDn), "ActiveDirectory:BaseDn is required")]
    [InlineData(nameof(ActiveDirectoryOptions.AllowedGroupSid), "ActiveDirectory:AllowedGroupSid is required")]
    [InlineData(nameof(ActiveDirectoryOptions.ServiceAccountUserName), "ActiveDirectory:ServiceAccountUserName is required")]
    [InlineData(nameof(ActiveDirectoryOptions.ServiceAccountPassword), "ActiveDirectory:ServiceAccountPassword is required")]
    public void Required_settings_must_be_present(string property, string expected)
    {
        var options = ValidOptions();
        typeof(ActiveDirectoryOptions).GetProperty(property)!.SetValue(options, " ");

        AssertFails(options, expected);
    }

    [Theory]
    [InlineData("10.0.0.10", "not an IP address")]
    [InlineData("fe80::1", "not an IP address")]
    [InlineData("ldaps://dc01.corp.example.com", "fully qualified DNS name")]
    [InlineData("dc01.corp.example.com:636", "fully qualified DNS name")]
    [InlineData("dc01", "fully qualified DNS name")]
    public void Server_must_be_a_dns_name_the_certificate_can_be_checked_against(string server, string expected)
    {
        var options = ValidOptions();
        options.ServerFqdn = server;

        AssertFails(options, expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Port_must_be_valid(int port)
    {
        var options = ValidOptions();
        options.Port = port;

        AssertFails(options, "Port must be between 1 and 65535");
    }

    [Theory]
    [InlineData("corp.example.com", "must be a distinguished name")]
    [InlineData("DC=other,DC=example,DC=com", "must be inside the domain corp.example.com")]
    [InlineData("DC=example,DC=com", "must be inside the domain corp.example.com")]
    public void Base_dn_must_be_inside_the_domain(string baseDn, string expected)
    {
        var options = ValidOptions();
        options.BaseDn = baseDn;

        AssertFails(options, expected);
    }

    [Theory]
    [InlineData("OU=Staff, DC=corp, DC=example, DC=com")]
    [InlineData("ou=staff,dc=CORP,dc=example,dc=com")]
    public void Base_dn_may_be_an_ou_in_any_case_and_spacing(string baseDn)
    {
        var options = ValidOptions();
        options.BaseDn = baseDn;

        Assert.True(Validate(options).Succeeded);
    }

    [Theory]
    [InlineData("Bim_Envanter")]
    [InlineData("S-1-5-32-544")]
    [InlineData("S-1-5-21-1-2-3")]
    [InlineData("S-1-5-21-1-2-3-4-5")]
    [InlineData("S-1-5-21-1-2-3-99999999999")]
    public void Allowed_group_must_be_a_domain_sid_not_a_name(string sid)
    {
        var options = ValidOptions();
        options.AllowedGroupSid = sid;

        AssertFails(options, "AllowedGroupSid must be the group's SID");
    }

    [Fact]
    public void Nested_group_policy_must_be_chosen_explicitly()
    {
        var options = ValidOptions();
        options.NestedGroupPolicy = null;

        AssertFails(options, "NestedGroupPolicy must be set explicitly");
    }

    [Theory]
    [InlineData(nameof(ActiveDirectoryOptions.Domain), "CHANGE-ME.local")]
    [InlineData(nameof(ActiveDirectoryOptions.ServerFqdn), "dc01.change-me.local")]
    [InlineData(nameof(ActiveDirectoryOptions.AllowedGroupSid), "CHANGE-ME")]
    [InlineData(nameof(ActiveDirectoryOptions.ServiceAccountPassword), "CHANGE-ME")]
    public void Placeholders_from_the_sample_configuration_are_refused(string property, string value)
    {
        var options = ValidOptions();
        typeof(ActiveDirectoryOptions).GetProperty(property)!.SetValue(options, value);

        AssertFails(options, $"ActiveDirectory:{property} still contains the CHANGE-ME placeholder");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Test")]
    public void Fake_directory_is_refused_outside_development(string environment)
    {
        var options = new ActiveDirectoryOptions { Mode = DirectoryMode.Fake };

        var result = Validate(options, environment);

        Assert.True(result.Failed);
        Assert.Contains("'Fake' is only allowed in Development", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Fake_directory_needs_no_server_settings_in_development()
    {
        var result = Validate(new ActiveDirectoryOptions { Mode = DirectoryMode.Fake }, "Development");

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Theory]
    [InlineData(0, 15, "ConnectTimeoutSeconds")]
    [InlineData(61, 15, "ConnectTimeoutSeconds")]
    [InlineData(10, 0, "OperationTimeoutSeconds")]
    [InlineData(10, 121, "OperationTimeoutSeconds")]
    public void Timeouts_must_be_bounded(int connect, int operation, string expected)
    {
        var options = ValidOptions();
        options.ConnectTimeoutSeconds = connect;
        options.OperationTimeoutSeconds = operation;

        AssertFails(options, expected);
    }

    [Fact]
    public void Pinned_ca_file_must_exist()
    {
        var options = ValidOptions();
        options.TrustedCaCertificatePath = Path.Combine(_tempDirectory, "missing.crt");

        AssertFails(options, "does not exist");
    }

    [Fact]
    public void Pinned_ca_file_must_be_a_certificate()
    {
        var options = ValidOptions();
        options.TrustedCaCertificatePath = Path.Combine(_tempDirectory, "garbage.crt");
        File.WriteAllText(options.TrustedCaCertificatePath, "not a certificate");

        AssertFails(options, "is not a readable PEM or DER certificate");
    }

    [Fact]
    public void Pinned_ca_file_must_be_a_ca_certificate()
    {
        var options = ValidOptions();
        options.TrustedCaCertificatePath = WriteCertificate("server.crt", isCertificateAuthority: false);

        AssertFails(options, "is not a CA certificate");
    }

    [Fact]
    public void Pinned_ca_must_be_the_root_not_an_issuing_ca()
    {
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rootRequest = new CertificateRequest("CN=Test Root CA", rootKey, HashAlgorithmName.SHA256);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

        using var issuingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var issuingRequest = new CertificateRequest("CN=Test Issuing CA", issuingKey, HashAlgorithmName.SHA256);
        issuingRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var issuing = issuingRequest.Create(root, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(20), [1, 2, 3, 4]);

        var options = ValidOptions();
        options.TrustedCaCertificatePath = Path.Combine(_tempDirectory, "issuing.crt");
        File.WriteAllText(options.TrustedCaCertificatePath, issuing.ExportCertificatePem());

        AssertFails(options, "is an issuing CA; pin the self-signed root CA");
    }

    [Fact]
    public void Pinned_ca_certificate_is_accepted()
    {
        var options = ValidOptions();
        options.TrustedCaCertificatePath = WriteCertificate("ca.crt", isCertificateAuthority: true);

        Assert.True(Validate(options).Succeeded, Validate(options).FailureMessage);
    }

    private static ActiveDirectoryOptions ValidOptions() => new()
    {
        Mode = DirectoryMode.Ldap,
        Domain = "corp.example.com",
        ServerFqdn = "dc01.corp.example.com",
        Port = 636,
        UseLdaps = true,
        BaseDn = "DC=corp,DC=example,DC=com",
        AllowedGroupSid = "S-1-5-21-1004336348-1177238915-682003330-1105",
        NestedGroupPolicy = NestedGroupPolicy.DirectMembershipOnly,
        ServiceAccountUserName = "svc.envanter",
        ServiceAccountPassword = "from-the-secret-store",
    };

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(ActiveDirectoryOptions options, string environment = "Production") =>
        new ActiveDirectoryOptionsValidator(new TestHostEnvironment(environment)).Validate(null, options);

    private static void AssertFails(ActiveDirectoryOptions options, string expected)
    {
        var result = Validate(options);

        Assert.True(result.Failed, $"Expected a failure containing '{expected}'.");
        Assert.Contains(expected, result.FailureMessage, StringComparison.Ordinal);
    }

    private string WriteCertificate(string fileName, bool isCertificateAuthority)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Test", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(isCertificateAuthority, false, 0, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

        var path = Path.Combine(_tempDirectory, fileName);
        File.WriteAllText(path, certificate.ExportCertificatePem());
        return path;
    }
}
