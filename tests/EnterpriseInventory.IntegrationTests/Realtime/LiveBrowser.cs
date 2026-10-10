using System.Net;
using System.Net.WebSockets;
using EnterpriseInventory.IntegrationTests.Api;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.SignalR.Client;

namespace EnterpriseInventory.IntegrationTests.Realtime;

/// <summary>
/// A browser's side of the API: one cookie jar for its requests and for its live connection to the inventory hub,
/// which it opens as the web app does (negotiation with the CSRF token, then the chosen transport).
/// </summary>
internal sealed class LiveBrowser : IAsyncDisposable
{
    public const string HubPath = "/hubs/inventory";

    public static readonly Uri BaseAddress = new("https://localhost");
    public static readonly Uri HubUrl = new(BaseAddress, HubPath);

    private readonly CookieContainer _cookies = new();
    private readonly TestApiFactory _api;
    private readonly Dictionary<string, string> _headers = [];
    private readonly List<HubConnection> _connections = [];

    public LiveBrowser(TestApiFactory api)
    {
        _api = api;
        Http = api.CreateDefaultClient(BaseAddress, new CookieContainerHandler(_cookies));
    }

    public HttpClient Http { get; }

    /// <summary>The CSRF token of the signed-in user, as the web app keeps it.</summary>
    public string? CsrfToken { get; private set; }

    public async Task SignInAsync(string userName, string password) => CsrfToken = await Http.SignInAsync(userName, password);

    /// <summary>Opens the app without signing in: the visitor gets an anonymous CSRF token.</summary>
    public async Task VisitAsync() => CsrfToken = await Http.GetCsrfTokenAsync();

    /// <summary>Signs in through the test scheme of <see cref="TestApiFactory"/>, which has no server-side session.</summary>
    public async Task ActAsAsync(string userName, string roles)
    {
        _headers[TestAuthHandler.UserHeader] = userName;
        _headers[TestAuthHandler.RolesHeader] = roles;
        foreach (var (name, value) in _headers)
        {
            Http.DefaultRequestHeaders.Add(name, value);
        }

        await VisitAsync();
    }

    /// <summary>A connection to the hub, not yet started, sent from <paramref name="origin"/> when given.</summary>
    public HubConnection Hub(HttpTransportType transport = HttpTransportType.LongPolling, string? origin = null, bool sendCsrfToken = true)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(HubUrl, transport, options =>
            {
                options.HttpMessageHandlerFactory = _ => new CookieContainerHandler(_cookies) { InnerHandler = _api.Server.CreateHandler() };
                foreach (var (name, value) in _headers)
                {
                    options.Headers[name] = value;
                }

                if (sendCsrfToken && CsrfToken is not null)
                {
                    options.Headers[AuthClient.CsrfHeader] = CsrfToken;
                }

                if (origin is not null)
                {
                    options.Headers["Origin"] = origin;
                }

                options.WebSocketFactory = (context, cancellationToken) => ConnectWebSocketAsync(context.Uri, origin, cancellationToken);
            })
            .Build();
        _connections.Add(connection);
        return connection;
    }

    /// <summary>Opens a WebSocket to the hub the way a page can, without negotiating first.</summary>
    public async ValueTask<WebSocket> ConnectWebSocketAsync(Uri uri, string? origin, CancellationToken cancellationToken)
    {
        var client = _api.Server.CreateWebSocketClient();
        var cookieHeader = _cookies.GetCookieHeader(BaseAddress);
        client.ConfigureRequest = request =>
        {
            foreach (var (name, value) in _headers)
            {
                request.Headers[name] = value;
            }

            if (cookieHeader.Length > 0)
            {
                request.Headers.Cookie = cookieHeader;
            }

            if (origin is not null)
            {
                request.Headers.Origin = origin;
            }
        };
        return await client.ConnectAsync(uri, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            await connection.DisposeAsync();
        }

        Http.Dispose();
    }
}

internal static class HubConnectionExtensions
{
    /// <summary>Completes when the connection closes, from either side; register before starting the connection.</summary>
    public static Task<Exception?> WhenClosed(this HubConnection connection)
    {
        var closed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += exception =>
        {
            closed.TrySetResult(exception);
            return Task.CompletedTask;
        };
        return closed.Task;
    }
}
