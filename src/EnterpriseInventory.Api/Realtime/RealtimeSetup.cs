using EnterpriseInventory.Api.Security;
using EnterpriseInventory.Application.Assets;

namespace EnterpriseInventory.Api.Realtime;

internal static class RealtimeSetup
{
    /// <summary>Clients send nothing but the handshake and pings, so a small limit is plenty.</summary>
    private const int MaxReceiveMessageBytes = 4 * 1024;

    public static IServiceCollection AddRealtime(this IServiceCollection services)
    {
        services.AddOptions<RealtimeOptions>()
            .BindConfiguration(RealtimeOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSignalR(options =>
        {
            // Error details stay in the server log; the client only learns that something failed.
            options.EnableDetailedErrors = false;
            options.MaximumReceiveMessageSize = MaxReceiveMessageBytes;
        });
        services.AddSingleton<HubConnectionRegistry>();
        services.AddHostedService<HubSessionMonitor>();

        // One instance both receives the committed changes and sends them in the background.
        services.AddSingleton<AssetChangeBroadcaster>();
        services.AddSingleton<IAssetChangeNotifier>(provider => provider.GetRequiredService<AssetChangeBroadcaster>());
        services.AddHostedService(provider => provider.GetRequiredService<AssetChangeBroadcaster>());

        return services;
    }

    public static IEndpointRouteBuilder MapRealtime(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<InventoryHub>(InventoryHub.Path, options =>
            {
                // A WebSocket outlives no cookie: it is closed when the authentication ticket expires.
                options.CloseOnAuthenticationExpiration = true;
            })
            .RequireAuthorization(AuthorizationPolicies.Administrator);

        return endpoints;
    }
}
