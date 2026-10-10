using System.Security.Claims;
using EnterpriseInventory.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace EnterpriseInventory.Api.Realtime;

/// <summary>
/// Live notifications about inventory changes, for signed-in administrators only. The hub has no methods a client
/// can call: it only sends (see <see cref="IInventoryClient"/>). Every connection belongs to a server-side session
/// and is closed when that session ends (<see cref="HubConnectionRegistry"/>, <see cref="HubSessionMonitor"/>).
/// </summary>
[Authorize(Policy = AuthorizationPolicies.Administrator)]
internal sealed partial class InventoryHub(HubConnectionRegistry connections, ILogger<InventoryHub> logger) : Hub<IInventoryClient>
{
    public const string Path = "/hubs/inventory";

    public override async Task OnConnectedAsync()
    {
        // Only a session can be closed when it ends; a connection without one is not kept open.
        var sessionKey = Context.User?.FindFirstValue(AppClaimTypes.SessionKey);
        if (string.IsNullOrEmpty(sessionKey))
        {
            LogRefusedWithoutSession(Context.User?.Identity?.Name);
            Context.Abort();
            return;
        }

        connections.Add(Context.ConnectionId, sessionKey, Context);
        LogConnected(Context.User?.Identity?.Name, Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Live connection of {UserName} refused: it belongs to no session")]
    private partial void LogRefusedWithoutSession(string? userName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Live connection {ConnectionId} of {UserName} opened")]
    private partial void LogConnected(string? userName, string connectionId);
}
