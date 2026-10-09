using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EnterpriseInventory.Infrastructure.Persistence;

/// <summary>
/// Healthy when the database can be reached and every migration has been applied, so a deployment whose
/// schema update was skipped reports itself as not ready.
/// </summary>
/// <remarks>
/// The readiness endpoint is anonymous, so the database is checked at most once at a time and a result is
/// reused for <see cref="ResultLifetime"/> after its check started; a flood of probes cannot open a
/// connection each. When SQL Server has gone away, SqlClient can block a thread for the whole connection
/// timeout and ignore cancellation (seen: 15 s with SQL Server stopped), so the shared check runs on the
/// thread pool and each caller stops waiting when its own health check timeout expires.
/// </remarks>
internal sealed class DatabaseHealthCheck(IServiceScopeFactory scopeFactory, TimeProvider timeProvider) : IHealthCheck
{
    public static readonly TimeSpan ResultLifetime = TimeSpan.FromSeconds(5);

    private readonly Lock _gate = new();
    private Task<HealthCheckResult>? _check;
    private DateTimeOffset _checkStartedAt;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        Task<HealthCheckResult> check;
        lock (_gate)
        {
            var now = timeProvider.GetUtcNow();
            if (_check is null || (_check.IsCompleted && now - _checkStartedAt >= ResultLifetime))
            {
                _checkStartedAt = now;
                _check = Task.Run(CheckDatabaseAsync, CancellationToken.None);
            }

            check = _check;
        }

        return check.WaitAsync(cancellationToken);
    }

    private async Task<HealthCheckResult> CheckDatabaseAsync()
    {
        try
        {
            // Resolved here so a missing connection string is reported as unhealthy rather than as an error.
            await using var scope = scopeFactory.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database;
            if (!await database.CanConnectAsync())
            {
                return HealthCheckResult.Unhealthy("The database cannot be reached.");
            }

            var pending = (await database.GetPendingMigrationsAsync()).ToList();
            return pending.Count == 0
                ? HealthCheckResult.Healthy("The database is reachable and up to date.")
                : HealthCheckResult.Unhealthy($"{pending.Count} migration(s) not applied: {string.Join(", ", pending)}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("The database check failed.", ex);
        }
    }
}
