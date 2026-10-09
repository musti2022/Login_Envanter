using EnterpriseInventory.Api.Security;
using EnterpriseInventory.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EnterpriseInventory.Api.Health;

internal static class HealthEndpoints
{
    public const string LivePath = "/api/health/live";
    public const string ReadyPath = "/api/health/ready";
    public const string DetailsPath = "/api/health";

    /// <summary>
    /// <list type="bullet">
    /// <item><c>/api/health/live</c>: the process is running. Checks no dependencies.</item>
    /// <item><c>/api/health/ready</c>: the database is reachable and migrated. 200 or 503.</item>
    /// <item><c>/api/health</c>: every check with its status and duration, for administrators only.</item>
    /// </list>
    /// The anonymous endpoints answer only <c>Healthy</c> or <c>Unhealthy</c>, so they reveal nothing about
    /// the environment; failure details are logged on the server.
    /// </summary>
    public static IEndpointRouteBuilder MapApiHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks(LivePath, new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous();

        endpoints.MapHealthChecks(ReadyPath, new HealthCheckOptions { Predicate = check => check.Tags.Contains(HealthCheckTags.Ready) })
            .AllowAnonymous();

        endpoints.MapHealthChecks(DetailsPath, new HealthCheckOptions { ResponseWriter = WriteDetailsAsync })
            .RequireAuthorization(AuthorizationPolicies.Administrator);

        return endpoints;
    }

    private static Task WriteDetailsAsync(HttpContext context, HealthReport report)
    {
        var response = new HealthDetailsResponse(
            report.Status.ToString(),
            Math.Round(report.TotalDuration.TotalMilliseconds),
            [.. report.Entries.Select(entry => new HealthCheckDetails(
                entry.Key,
                entry.Value.Status.ToString(),
                Math.Round(entry.Value.Duration.TotalMilliseconds),
                entry.Value.Description))]);
        return context.Response.WriteAsJsonAsync(response);
    }

    /// <summary>Exception messages are left out on purpose; they are in the server log.</summary>
    private sealed record HealthDetailsResponse(string Status, double TotalDurationMs, IReadOnlyList<HealthCheckDetails> Checks);

    private sealed record HealthCheckDetails(string Name, string Status, double DurationMs, string? Description);
}
