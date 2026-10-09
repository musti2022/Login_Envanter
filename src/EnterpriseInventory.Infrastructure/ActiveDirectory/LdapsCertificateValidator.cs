using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace EnterpriseInventory.Infrastructure.ActiveDirectory;

/// <summary>
/// Decides whether the domain controller's LDAPS certificate is trusted. It must be issued for the configured
/// server name, be inside its validity period, allow server authentication and chain to a trusted root: the
/// configured CA when one is pinned, otherwise a CA in the server's trust store. There is deliberately no
/// setting that relaxes any of this.
/// </summary>
internal sealed class LdapsCertificateValidator(X509Certificate2? trustedRoot, bool checkRevocation)
{
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

    /// <summary>
    /// Checks the certificate presented in a TLS handshake. <paramref name="errors"/> and <paramref name="chain"/>
    /// are what TLS reported after checking the name and building the chain against the machine's trust store.
    /// </summary>
    public CertificateValidationResult Validate(X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        if (certificate is null || errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
        {
            return CertificateValidationResult.Rejected("the server presented no certificate");
        }

        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
        {
            return CertificateValidationResult.Rejected("the certificate is not issued for the configured server name");
        }

        if (trustedRoot is null)
        {
            return errors == SslPolicyErrors.None
                ? CertificateValidationResult.Trusted
                : CertificateValidationResult.Rejected("the certificate chain is not trusted: " + Describe(chain));
        }

        return ValidateAgainstPinnedRoot(certificate, chain, trustedRoot);
    }

    private CertificateValidationResult ValidateAgainstPinnedRoot(X509Certificate certificate, X509Chain? reported, X509Certificate2 root)
    {
        var leaf = certificate as X509Certificate2;
        using var copy = leaf is null ? new X509Certificate2(certificate) : null;
        leaf ??= copy!;

        using var chain = new X509Chain();
        var policy = chain.ChainPolicy;
        policy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        policy.CustomTrustStore.Add(root);
        policy.RevocationMode = checkRevocation ? X509RevocationMode.Online : X509RevocationMode.NoCheck;
        policy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
        policy.ApplicationPolicy.Add(new Oid(ServerAuthenticationOid));
        policy.VerificationFlags = X509VerificationFlags.NoFlag;

        // Intermediate CAs the server sent are candidates for building the chain; they grant no trust by
        // themselves, since the chain still has to end at the pinned root.
        if (reported is not null)
        {
            policy.ExtraStore.AddRange(reported.ChainPolicy.ExtraStore);
            foreach (var element in reported.ChainElements.Skip(1))
            {
                policy.ExtraStore.Add(element.Certificate);
            }
        }

        return chain.Build(leaf)
            ? CertificateValidationResult.Trusted
            : CertificateValidationResult.Rejected("the certificate does not chain to the configured CA: " + Describe(chain));
    }

    private static string Describe(X509Chain? chain)
    {
        var statuses = chain?.ChainStatus
            .Where(status => status.Status != X509ChainStatusFlags.NoError)
            .Select(status => status.Status.ToString())
            .Distinct()
            .ToList();
        return statuses is { Count: > 0 } ? string.Join(", ", statuses) : "unknown chain error";
    }
}

internal sealed record CertificateValidationResult(bool IsTrusted, string? RejectionReason)
{
    public static readonly CertificateValidationResult Trusted = new(true, null);

    public static CertificateValidationResult Rejected(string reason) => new(false, reason);
}
