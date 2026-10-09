using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EnterpriseInventory.Infrastructure.ActiveDirectory;

/// <summary>
/// Runs at startup (ValidateOnStart), so a server with missing, placeholder or insecure directory settings never
/// starts accepting sign-ins. Plain LDAP cannot be configured, and the fake directory is refused outside
/// Development.
/// </summary>
internal sealed partial class ActiveDirectoryOptionsValidator(IHostEnvironment environment) : IValidateOptions<ActiveDirectoryOptions>
{
    private const string Placeholder = "CHANGE-ME";

    public ValidateOptionsResult Validate(string? name, ActiveDirectoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();
        if (!Enum.IsDefined(options.Mode))
        {
            errors.Add($"ActiveDirectory:Mode has an unknown value '{options.Mode}'.");
        }
        else if (options.Mode == DirectoryMode.Fake)
        {
            if (!environment.IsDevelopment())
            {
                errors.Add($"ActiveDirectory:Mode 'Fake' is only allowed in Development, not in '{environment.EnvironmentName}'.");
            }

            ValidateFakeUsers(options, errors);
            return Result(errors);
        }

        if (options.FakeUsers.Count > 0 && !environment.IsDevelopment())
        {
            errors.Add("ActiveDirectory:FakeUsers must not be configured outside Development.");
        }

        ValidateConnection(options, errors);
        ValidateAccess(options, errors);
        ValidateCertificateTrust(options, errors);

        if (options.ConnectTimeoutSeconds is < 1 or > 60)
        {
            errors.Add("ActiveDirectory:ConnectTimeoutSeconds must be between 1 and 60.");
        }

        if (options.OperationTimeoutSeconds is < 1 or > 120)
        {
            errors.Add("ActiveDirectory:OperationTimeoutSeconds must be between 1 and 120.");
        }

        return Result(errors);
    }

    private static void ValidateFakeUsers(ActiveDirectoryOptions options, List<string> errors)
    {
        for (var i = 0; i < options.FakeUsers.Count; i++)
        {
            var user = options.FakeUsers[i];
            if (!DirectoryValues.TryGetSamAccountName(user.UserName, "fake.invalid", out _))
            {
                errors.Add($"ActiveDirectory:FakeUsers:{i}:UserName must be a logon name of at most {DirectoryValues.SamAccountNameMaxLength} characters.");
            }

            if (string.IsNullOrEmpty(user.Password))
            {
                errors.Add($"ActiveDirectory:FakeUsers:{i}:Password is required.");
            }
        }
    }

    private static void ValidateConnection(ActiveDirectoryOptions options, List<string> errors)
    {
        if (RequireValue(options.Domain, "Domain", errors) && !DnsName().IsMatch(options.Domain))
        {
            errors.Add("ActiveDirectory:Domain must be the domain's DNS name, e.g. corp.example.com.");
        }

        if (RequireValue(options.ServerFqdn, "ServerFqdn", errors))
        {
            if (IPAddress.TryParse(options.ServerFqdn, out _))
            {
                errors.Add("ActiveDirectory:ServerFqdn must be the domain controller's DNS name, not an IP address; the certificate is checked against it.");
            }
            else if (!DnsName().IsMatch(options.ServerFqdn))
            {
                errors.Add("ActiveDirectory:ServerFqdn must be a fully qualified DNS name such as dc01.corp.example.com, without a scheme or port.");
            }
        }

        if (options.Port is < 1 or > 65535)
        {
            errors.Add("ActiveDirectory:Port must be between 1 and 65535.");
        }

        if (!options.UseLdaps)
        {
            errors.Add("ActiveDirectory:UseLdaps must be true. Plain LDAP would send passwords without verified TLS and is not supported.");
        }

        if (RequireValue(options.BaseDn, "BaseDn", errors))
        {
            if (!DistinguishedName().IsMatch(options.BaseDn))
            {
                errors.Add("ActiveDirectory:BaseDn must be a distinguished name such as DC=corp,DC=example,DC=com.");
            }
            else if (DnsName().IsMatch(options.Domain)
                && !DistinguishedNames.IsSameOrInside(options.BaseDn, DistinguishedNames.FromDnsDomain(options.Domain)))
            {
                errors.Add($"ActiveDirectory:BaseDn must be inside the domain {options.Domain} (end with {DistinguishedNames.FromDnsDomain(options.Domain)}).");
            }
        }
    }

    private static void ValidateAccess(ActiveDirectoryOptions options, List<string> errors)
    {
        if (RequireValue(options.AllowedGroupSid, "AllowedGroupSid", errors) && !IsDomainSid(options.AllowedGroupSid))
        {
            errors.Add("ActiveDirectory:AllowedGroupSid must be the group's SID in the form S-1-5-21-<domain>-<rid>.");
        }

        if (options.NestedGroupPolicy is null)
        {
            errors.Add("ActiveDirectory:NestedGroupPolicy must be set explicitly to DirectMembershipOnly or IncludeNested.");
        }
        else if (!Enum.IsDefined(options.NestedGroupPolicy.Value))
        {
            errors.Add($"ActiveDirectory:NestedGroupPolicy has an unknown value '{options.NestedGroupPolicy}'.");
        }

        RejectPlaceholder(options.ServiceAccountUserName, "ServiceAccountUserName", errors);
        RejectPlaceholder(options.ServiceAccountPassword, "ServiceAccountPassword", errors);
    }

    private static void ValidateCertificateTrust(ActiveDirectoryOptions options, List<string> errors)
    {
        var path = options.TrustedCaCertificatePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!File.Exists(path))
        {
            errors.Add($"ActiveDirectory:TrustedCaCertificatePath '{path}' does not exist.");
            return;
        }

        try
        {
            using var certificate = X509CertificateLoader.LoadCertificateFromFile(path);
            var basicConstraints = certificate.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
            if (basicConstraints is not { CertificateAuthority: true })
            {
                errors.Add($"ActiveDirectory:TrustedCaCertificatePath '{path}' is not a CA certificate.");
            }
        }
        catch (CryptographicException)
        {
            errors.Add($"ActiveDirectory:TrustedCaCertificatePath '{path}' is not a readable PEM or DER certificate.");
        }
    }

    private static bool RequireValue(string? value, string key, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"ActiveDirectory:{key} is required.");
            return false;
        }

        return RejectPlaceholder(value, key, errors);
    }

    private static bool RejectPlaceholder(string? value, string key, List<string> errors)
    {
        if (value?.Contains(Placeholder, StringComparison.OrdinalIgnoreCase) == true)
        {
            errors.Add($"ActiveDirectory:{key} still contains the {Placeholder} placeholder.");
            return false;
        }

        return true;
    }

    private static bool IsDomainSid(string value)
    {
        var match = DomainSid().Match(value);
        return match.Success && match.Groups["sub"].Captures.All(c => uint.TryParse(c.Value, NumberStyles.None, CultureInfo.InvariantCulture, out _));
    }

    private static ValidateOptionsResult Result(List<string> errors) =>
        errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);

    // At least two labels of letters, digits and inner hyphens; at most 253 characters.
    [GeneratedRegex(@"^(?=.{1,253}$)[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)+$")]
    private static partial Regex DnsName();

    // RDNs such as OU=Users or DC=corp separated by commas; commas inside values must be escaped.
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9-]*=(?:[^,\\]|\\.)+(?:,\s*[A-Za-z][A-Za-z0-9-]*=(?:[^,\\]|\\.)+)*$")]
    private static partial Regex DistinguishedName();

    // Domain account SIDs: S-1-5-21, three domain sub-authorities and the RID.
    [GeneratedRegex(@"^S-1-5-21(?:-(?<sub>\d{1,10})){4}$")]
    private static partial Regex DomainSid();
}
