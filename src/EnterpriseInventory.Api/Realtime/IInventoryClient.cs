namespace EnterpriseInventory.Api.Realtime;

/// <summary>
/// What the inventory hub sends to the screens. A notification only says that an asset changed; the screen asks the
/// API for the asset as it is now, so it never shows data the user could not read with a normal request.
/// </summary>
public interface IInventoryClient
{
    Task AssetCreated(AssetNotification notification);

    Task AssetUpdated(AssetNotification notification);

    Task AssetArchived(AssetNotification notification);

    Task AssetAssigned(AssetNotification notification);

    Task AssetReturned(AssetNotification notification);

    Task AssetLocationChanged(AssetNotification notification);
}

/// <param name="AssetId">The asset that changed.</param>
/// <param name="OccurredAt">When the change was committed.</param>
public sealed record AssetNotification(int AssetId, DateTimeOffset OccurredAt);
