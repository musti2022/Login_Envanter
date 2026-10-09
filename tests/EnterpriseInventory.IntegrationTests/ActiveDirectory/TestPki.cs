using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace EnterpriseInventory.IntegrationTests.ActiveDirectory;

/// <summary>Creates throwaway CAs and server certificates in memory for the certificate validation tests.</summary>
internal static class TestPki
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public static X509Certificate2 CreateCa(string name, X509Certificate2? issuer = null)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        if (issuer is null)
        {
            return Reload(request.CreateSelfSigned(Now.AddDays(-30), Now.AddYears(2)));
        }

        using var signed = request.Create(issuer, Now.AddDays(-20), Now.AddYears(1), NewSerialNumber());
        return Reload(signed.CopyWithPrivateKey(key));
    }

    public static X509Certificate2 CreateServerCertificate(
        X509Certificate2 issuer,
        string serverName,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null,
        bool serverAuthentication = true)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={serverName}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid(serverAuthentication ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2")], false));
        var names = new SubjectAlternativeNameBuilder();
        if (IPAddress.TryParse(serverName, out var address))
        {
            names.AddIpAddress(address);
        }
        else
        {
            names.AddDnsName(serverName);
        }

        request.CertificateExtensions.Add(names.Build());

        // Signed through a generator rather than Create(issuer, ...), which refuses issuers that are not CAs:
        // the tests need such certificates too.
        using var issuerKey = issuer.GetECDsaPrivateKey()
            ?? throw new ArgumentException("The issuer needs an ECDSA private key.", nameof(issuer));
        using var signed = request.Create(
            issuer.SubjectName,
            X509SignatureGenerator.CreateForECDsa(issuerKey),
            notBefore ?? Now.AddDays(-1),
            notAfter ?? Now.AddDays(90),
            NewSerialNumber());
        return Reload(signed.CopyWithPrivateKey(key));
    }

    public static string WritePem(X509Certificate2 certificate, string directory)
    {
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}.crt");
        File.WriteAllText(path, certificate.ExportCertificatePem());
        return path;
    }

    private static byte[] NewSerialNumber() => RandomNumberGenerator.GetBytes(16);

    // A PKCS#12 round trip gives a persisted key, which SslStream needs on Windows to serve the certificate.
    private static X509Certificate2 Reload(X509Certificate2 certificate)
    {
        using (certificate)
        {
            return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), password: null);
        }
    }
}
