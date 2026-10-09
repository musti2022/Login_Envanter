using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using EnterpriseInventory.Infrastructure.ActiveDirectory;

namespace EnterpriseInventory.IntegrationTests.ActiveDirectory;

/// <summary>
/// The decisions of <see cref="LdapsCertificateValidator"/> for the errors TLS reports. With a pinned CA, TLS
/// reports a chain error (the CA is not in the machine store) and the validator builds the chain itself.
/// </summary>
public sealed class LdapsCertificateValidatorTests : IDisposable
{
    private const string Server = "dc1.envanter.test";
    private const SslPolicyErrors UnknownRoot = SslPolicyErrors.RemoteCertificateChainErrors;

    private readonly X509Certificate2 _ca = TestPki.CreateCa("Test AD CA");
    private readonly X509Certificate2 _otherCa = TestPki.CreateCa("Some other CA");

    public void Dispose()
    {
        _ca.Dispose();
        _otherCa.Dispose();
    }

    [Fact]
    public void Certificate_from_the_pinned_ca_is_trusted()
    {
        using var certificate = TestPki.CreateServerCertificate(_ca, Server);

        var result = Pinned().Validate(certificate, null, UnknownRoot);

        Assert.True(result.IsTrusted, result.RejectionReason);
    }

    [Fact]
    public void Certificate_through_an_intermediate_sent_by_the_server_is_trusted()
    {
        using var intermediate = TestPki.CreateCa("Issuing CA", issuer: _ca);
        using var certificate = TestPki.CreateServerCertificate(intermediate, Server);
        using var reported = new X509Chain();
        reported.ChainPolicy.ExtraStore.Add(intermediate);

        var result = Pinned().Validate(certificate, reported, UnknownRoot);

        Assert.True(result.IsTrusted, result.RejectionReason);
    }

    [Fact]
    public void Certificate_from_another_ca_is_rejected()
    {
        using var certificate = TestPki.CreateServerCertificate(_otherCa, Server);

        var result = Pinned().Validate(certificate, null, UnknownRoot);

        AssertRejected(result, "does not chain to the configured CA");
    }

    [Fact]
    public void Another_ca_cannot_vouch_for_itself_by_being_sent_along()
    {
        using var certificate = TestPki.CreateServerCertificate(_otherCa, Server);
        using var reported = new X509Chain();
        reported.ChainPolicy.ExtraStore.Add(_otherCa);

        var result = Pinned().Validate(certificate, reported, UnknownRoot);

        AssertRejected(result, "does not chain to the configured CA");
    }

    [Fact]
    public void Name_mismatch_is_rejected_even_when_the_chain_is_good()
    {
        using var certificate = TestPki.CreateServerCertificate(_ca, "other.envanter.test");

        var result = Pinned().Validate(certificate, null, UnknownRoot | SslPolicyErrors.RemoteCertificateNameMismatch);

        AssertRejected(result, "not issued for the configured server name");
    }

    [Fact]
    public void Name_mismatch_is_rejected_with_the_machine_trust_store_too()
    {
        using var certificate = TestPki.CreateServerCertificate(_ca, "other.envanter.test");

        var result = new LdapsCertificateValidator(null, checkRevocation: false)
            .Validate(certificate, null, SslPolicyErrors.RemoteCertificateNameMismatch);

        AssertRejected(result, "not issued for the configured server name");
    }

    [Fact]
    public void Expired_certificate_is_rejected()
    {
        using var certificate = TestPki.CreateServerCertificate(
            _ca, Server, notBefore: DateTimeOffset.UtcNow.AddDays(-20), notAfter: DateTimeOffset.UtcNow.AddDays(-1));

        var result = Pinned().Validate(certificate, null, UnknownRoot);

        AssertRejected(result, "NotTimeValid");
    }

    [Fact]
    public void Certificate_that_is_not_valid_yet_is_rejected()
    {
        using var certificate = TestPki.CreateServerCertificate(
            _ca, Server, notBefore: DateTimeOffset.UtcNow.AddDays(1), notAfter: DateTimeOffset.UtcNow.AddDays(30));

        var result = Pinned().Validate(certificate, null, UnknownRoot);

        AssertRejected(result, "NotTimeValid");
    }

    [Fact]
    public void Certificate_without_server_authentication_usage_is_rejected()
    {
        using var certificate = TestPki.CreateServerCertificate(_ca, Server, serverAuthentication: false);

        var result = Pinned().Validate(certificate, null, UnknownRoot);

        AssertRejected(result, "NotValidForUsage");
    }

    [Fact]
    public void Certificate_signed_by_a_server_certificate_is_rejected()
    {
        using var server = TestPki.CreateServerCertificate(_ca, "not-a-ca.envanter.test");
        using var certificate = TestPki.CreateServerCertificate(server, Server);
        using var reported = new X509Chain();
        reported.ChainPolicy.ExtraStore.Add(server);

        var result = Pinned().Validate(certificate, reported, UnknownRoot);

        Assert.False(result.IsTrusted);
    }

    [Fact]
    public void Unknown_revocation_status_is_rejected_when_revocation_is_checked()
    {
        // The test CA publishes no revocation list, so revocation cannot be confirmed: fail closed.
        using var certificate = TestPki.CreateServerCertificate(_ca, Server);

        var result = new LdapsCertificateValidator(_ca, checkRevocation: true).Validate(certificate, null, UnknownRoot);

        AssertRejected(result, "RevocationStatusUnknown");
    }

    [Fact]
    public void Missing_certificate_is_rejected()
    {
        var result = Pinned().Validate(null, null, SslPolicyErrors.RemoteCertificateNotAvailable);

        AssertRejected(result, "presented no certificate");
    }

    [Fact]
    public void Without_a_pinned_ca_only_a_clean_tls_result_is_trusted()
    {
        using var certificate = TestPki.CreateServerCertificate(_ca, Server);
        var validator = new LdapsCertificateValidator(null, checkRevocation: false);

        Assert.True(validator.Validate(certificate, null, SslPolicyErrors.None).IsTrusted);
        AssertRejected(validator.Validate(certificate, null, UnknownRoot), "chain is not trusted");
    }

    private LdapsCertificateValidator Pinned() => new(_ca, checkRevocation: false);

    private static void AssertRejected(CertificateValidationResult result, string reason)
    {
        Assert.False(result.IsTrusted);
        Assert.Contains(reason, result.RejectionReason, StringComparison.Ordinal);
    }
}
