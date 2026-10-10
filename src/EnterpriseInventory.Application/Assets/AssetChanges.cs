using Microsoft.Extensions.Logging;

namespace EnterpriseInventory.Application.Assets;

public enum AssetChange
{
    Created = 0,
    Updated = 1,
    Archived = 2,
    Assigned = 3,
    Returned = 4,
    LocationChanged = 5,
}

/// <summary>A change to an asset that has been committed.</summary>
/// <param name="OccurredAt">When the change was saved: the asset's last update (its creation for a new asset).</param>
public sealed record AssetChanged(AssetChange Change, int AssetId, DateTimeOffset OccurredAt);

/// <summary>
/// Tells the open screens that an asset changed (live notifications). Delivery is best effort and must not make the
/// caller wait: a screen that misses a notification catches up when it reloads or reconnects.
/// </summary>
public interface IAssetChangeNotifier
{
    void Notify(AssetChanged change);
}

/// <summary>
/// Announces a write once it has been committed, and only then: refused writes (validation, conflict, rules, not found)
/// and writes that changed nothing are not announced, and a write that failed has thrown before getting here. A
/// failing notifier is logged and otherwise ignored, so the caller still hears that the committed change succeeded.
/// </summary>
public sealed partial class AssetChangePublisher(IAssetChangeNotifier notifier, ILogger<AssetChangePublisher> logger)
{
    /// <param name="readVersion">
    /// The row version the caller changed; when the saved asset still has it, nothing was written and nothing is announced.
    /// </param>
    public AssetWriteResult Announce(AssetChange change, AssetWriteResult result, string? readVersion = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result is not { Outcome: AssetWriteOutcome.Succeeded, Asset: { } asset } || IsUnchanged(readVersion, asset))
        {
            return result;
        }

        try
        {
            notifier.Notify(new AssetChanged(change, asset.Id, asset.UpdatedAt ?? asset.CreatedAt));
        }
        catch (Exception ex)
        {
            LogNotifyFailed(ex, change, asset.Id);
        }

        return result;
    }

    // SQL Server gives the row a new version on every write, so an unchanged version means nothing was saved.
    private static bool IsUnchanged(string? readVersion, AssetDetails asset) =>
        AssetRowVersion.TryDecode(readVersion, out var read)
        && AssetRowVersion.TryDecode(asset.RowVersion, out var saved)
        && read.AsSpan().SequenceEqual(saved);

    [LoggerMessage(Level = LogLevel.Error, Message = "The committed change {Change} of asset {AssetId} could not be announced")]
    private partial void LogNotifyFailed(Exception exception, AssetChange change, int assetId);
}

/// <summary>For hosts without live notifications (tools, tests): announcements go nowhere.</summary>
public sealed class NoAssetChangeNotifier : IAssetChangeNotifier
{
    public void Notify(AssetChanged change)
    {
    }
}
