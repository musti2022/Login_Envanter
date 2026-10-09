using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Novell.Directory.Ldap;

namespace EnterpriseInventory.Infrastructure.ActiveDirectory;

/// <summary>
/// Opens an LDAPS connection with the full certificate check and reads the directory's RootDSE anonymously, so it
/// needs no credentials. It also catches a BaseDn that is not in the directory's domain. Failure is reported as
/// degraded rather than unhealthy: people already signed in can keep working while new sign-ins fail closed.
/// It is not part of readiness and only runs on the administrator health endpoint.
/// </summary>
internal sealed class ActiveDirectoryHealthCheck(ILdapConnectionFactory connectionFactory, IOptions<ActiveDirectoryOptions> options)
    : IHealthCheck
{
    private static readonly string[] RootDseAttributes = ["defaultNamingContext"];

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = options.Value;
        var failureStatus = context.Registration.FailureStatus;
        if (settings.Mode == DirectoryMode.Fake)
        {
            return HealthCheckResult.Healthy("The Development-only fake directory is in use; no domain controller is contacted.");
        }

        try
        {
            using var connection = await connectionFactory.ConnectAsync(cancellationToken).ConfigureAwait(false);
            var rootDse = await connection.SearchAsync(string.Empty, LdapConnection.ScopeBase, "(objectClass=*)", RootDseAttributes, cancellationToken)
                .ConfigureAwait(false);
            var namingContext = rootDse.Entries.Count == 1 ? rootDse.Entries[0].GetAttributeSet().Find("defaultNamingContext")?.StringValue : null;

            if (string.IsNullOrEmpty(namingContext) || !DistinguishedNames.IsSameOrInside(settings.BaseDn, namingContext))
            {
                return new HealthCheckResult(
                    failureStatus,
                    $"LDAPS works, but BaseDn '{settings.BaseDn}' is not inside the directory's domain '{namingContext}'.");
            }

            return HealthCheckResult.Healthy($"LDAPS connection to {settings.ServerFqdn}:{settings.Port} works and its certificate is trusted.");
        }
        catch (DirectoryUnavailableException ex)
        {
            return new HealthCheckResult(failureStatus, ex.Message);
        }
        catch (LdapException ex)
        {
            return new HealthCheckResult(failureStatus, $"The directory refused the RootDSE query (LDAP result {ex.ResultCode}).");
        }
    }
}
