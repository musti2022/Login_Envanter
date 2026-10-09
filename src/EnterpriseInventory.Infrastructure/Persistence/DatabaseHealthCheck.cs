using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EnterpriseInventory.Infrastructure.Persistence;

/// <summary>
/// Healthy when the database can be reached and every migration has been applied, so a deployment whose
/// schema update was skipped reports itself as not ready.
/// </summary>
/// <remarks>
/// When a pooled connection's server has gone away, SqlClient can reconnect synchronously and ignore the
/// token for the whole connection timeout (seen: 15 s with SQL Server stopped). The check therefore runs on
/// the thread pool and stops waiting when the health check timeout cancels the token.
/// </remarks>
internal sealed class DatabaseHealthCheck(IServiceProvider services) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.Run(() => CheckDatabaseAsync(cancellationToken), cancellationToken).WaitAsync(cancellationToken);

    private async Task<HealthCheckResult> CheckDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Resolved here so a missing connection string is reported as unhealthy rather than as an error.
            var database = services.GetRequiredService<ApplicationDbContext>().Database;
            if (!await database.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy("The database cannot be reached.");
            }

            var pending = (await database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            return pending.Count == 0
                ? HealthCheckResult.Healthy("The database is reachable and up to date.")
                : HealthCheckResult.Unhealthy($"{pending.Count} migration(s) not applied: {string.Join(", ", pending)}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("The database check failed.", ex);
        }
    }
}
