using System.Threading.Channels;
using EnterpriseInventory.Application.Assets;
using Microsoft.AspNetCore.SignalR;

namespace EnterpriseInventory.Api.Realtime;

/// <summary>
/// Sends committed asset changes to every open inventory hub connection. The request that made the change only queues
/// it and returns; sending happens here in the background, so a slow or failing connection never delays or fails a
/// committed change. Every connection belongs to a signed-in administrator, and all of them may read every asset.
/// </summary>
/// <remarks>
/// The queue lives in this server's memory: a change queued just before the process stops is not sent, and with more
/// than one server each one reaches only its own connections. Screens catch up when they reload or reconnect.
/// </remarks>
internal sealed partial class AssetChangeBroadcaster(IHubContext<InventoryHub, IInventoryClient> hub, ILogger<AssetChangeBroadcaster> logger)
    : BackgroundService, IAssetChangeNotifier
{
    /// <summary>Far more than a burst of changes needs; beyond it new changes are dropped (and logged), never waited for.</summary>
    private const int QueueCapacity = 1_000;

    private readonly Channel<AssetChanged> _queue = Channel.CreateBounded<AssetChanged>(
        new BoundedChannelOptions(QueueCapacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    public void Notify(AssetChanged change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (!_queue.Writer.TryWrite(change))
        {
            LogDropped(change.Change, change.AssetId);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var change in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await SendAsync(change);
                }
                catch (Exception ex)
                {
                    LogSendFailed(ex, change.Change, change.AssetId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
    }

    private Task SendAsync(AssetChanged change)
    {
        var notification = new AssetNotification(change.AssetId, change.OccurredAt);
        var everyone = hub.Clients.All;
        return change.Change switch
        {
            AssetChange.Created => everyone.AssetCreated(notification),
            AssetChange.Updated => everyone.AssetUpdated(notification),
            AssetChange.Archived => everyone.AssetArchived(notification),
            AssetChange.Assigned => everyone.AssetAssigned(notification),
            AssetChange.Returned => everyone.AssetReturned(notification),
            AssetChange.LocationChanged => everyone.AssetLocationChanged(notification),
            _ => throw new ArgumentOutOfRangeException(nameof(change), change.Change, "Unknown asset change."),
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The notification queue is full; change {Change} of asset {AssetId} is not announced")]
    private partial void LogDropped(AssetChange change, int assetId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Change {Change} of asset {AssetId} could not be sent to the live connections")]
    private partial void LogSendFailed(Exception exception, AssetChange change, int assetId);
}
