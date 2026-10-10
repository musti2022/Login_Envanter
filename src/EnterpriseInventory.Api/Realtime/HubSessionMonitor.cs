using EnterpriseInventory.Api.Http;
using EnterpriseInventory.Application.Authentication;
using Microsoft.Extensions.Options;
using Serilog.Context;

namespace EnterpriseInventory.Api.Realtime;

/// <summary>
/// An open live connection sends no requests that could be refused, so its session is checked here instead: on every
/// <see cref="RealtimeOptions.SessionCheckInterval"/>, each session with open connections goes through the same checks
/// as a request (timeouts, sign-out, the periodic directory re-check) without counting as activity. Connections of a
/// session that is no longer valid are closed; when a check cannot be made, they are closed too.
/// </summary>
internal sealed partial class HubSessionMonitor(
    HubConnectionRegistry connections,
    IServiceScopeFactory scopes,
    IOptions<RealtimeOptions> options,
    TimeProvider timeProvider,
    ILogger<HubSessionMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.SessionCheckInterval, timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await CheckOpenSessionsAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
    }

    private async Task CheckOpenSessionsAsync(CancellationToken cancellationToken)
    {
        foreach (var sessionKey in connections.SessionKeys())
        {
            await using var scope = scopes.CreateAsyncScope();
            var operation = scope.ServiceProvider.GetRequiredService<BackgroundOperation>();
            using (LogContext.PushProperty(CorrelationIdMiddleware.LogPropertyName, operation.CorrelationId))
            {
                if (!await IsValidAsync(scope.ServiceProvider, sessionKey, cancellationToken))
                {
                    var closed = connections.Close(sessionKey);
                    LogConnectionsClosed(closed);
                }
            }
        }
    }

    private async Task<bool> IsValidAsync(IServiceProvider services, string sessionKey, CancellationToken cancellationToken)
    {
        try
        {
            var result = await services.GetRequiredService<IUserSessionService>().CheckAsync(sessionKey, cancellationToken);
            return result.IsValid;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Fail closed: a connection whose session cannot be confirmed is closed. The screen reconnects, and the
            // new connection is only accepted for a session that is still valid.
            LogCheckFailed(ex);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Closed {ConnectionCount} live connection(s) of a session that is no longer valid")]
    private partial void LogConnectionsClosed(int connectionCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "The session of a live connection could not be checked; its connections are closed")]
    private partial void LogCheckFailed(Exception exception);
}
