using System.Net;
using System.Net.WebSockets;
using EnterpriseInventory.Application.Authentication;
using EnterpriseInventory.Domain.Auditing;
using EnterpriseInventory.Domain.Users;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseInventory.IntegrationTests.Realtime;

/// <summary>
/// Day 26: the inventory hub accepts only signed-in administrators with a valid server-side session, refuses other
/// sites, offers nothing to call, and closes a connection when its session ends: at sign-out, after the idle timeout
/// and when the directory re-check takes the user's access away. Time is moved by a test clock; the directory is a
/// stand-in whose answer each test controls.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class InventoryHubTests(SqlServerDatabaseFixture database) : IAsyncLifetime
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private readonly TestClock _clock = new(DateTimeOffset.UtcNow);
    private readonly ControllableDirectory _directory = new();
    private readonly List<IAsyncDisposable> _resources = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _resources.Reverse();
        foreach (var resource in _resources)
        {
            await resource.DisposeAsync();
        }
    }

    // --- Who may connect ---------------------------------------------------------------------------------------

    [Fact]
    public async Task An_anonymous_visitor_cannot_connect_with_any_transport()
    {
        var browser = Browser(new TestApiFactory());
        await browser.VisitAsync();

        foreach (var transport in new[] { HttpTransportType.LongPolling, HttpTransportType.ServerSentEvents, HttpTransportType.WebSockets })
        {
            var refused = await Assert.ThrowsAsync<HttpRequestException>(() => browser.Hub(transport).StartAsync());
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        }

        // A WebSocket opened straight away, without negotiating, is refused too.
        var direct = await Assert.ThrowsAnyAsync<Exception>(async () => await browser.ConnectWebSocketAsync(WebSocketUrl, origin: null, CancellationToken.None));
        Assert.Contains("401", direct.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_signed_in_user_without_the_administrator_role_is_refused()
    {
        var browser = Browser(new TestApiFactory());
        await browser.ActAsAsync("okur.kullanici", "Reader");

        var refused = await Assert.ThrowsAsync<HttpRequestException>(() => browser.Hub().StartAsync());

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task A_connection_that_belongs_to_no_session_is_closed_at_once()
    {
        // Only the test scheme can sign someone in without a session; the API's own cookie always carries one.
        var browser = Browser(new TestApiFactory());
        await browser.ActAsAsync("ayse.admin", "Administrator");
        var hub = browser.Hub();
        var closed = hub.WhenClosed();

        try
        {
            await hub.StartAsync();
        }
        catch (Exception ex) when (ex is HubException or InvalidOperationException or HttpRequestException)
        {
            // The server may close it before the start completes.
        }

        await closed.WaitAsync(Patience);
        Assert.Equal(HubConnectionState.Disconnected, hub.State);
    }

    [SqlServerFact]
    public async Task An_administrator_with_a_session_connects_with_every_transport()
    {
        var browser = await SignedInBrowserAsync(Api());

        foreach (var transport in new[] { HttpTransportType.LongPolling, HttpTransportType.ServerSentEvents, HttpTransportType.WebSockets })
        {
            var hub = browser.Hub(transport);
            await hub.StartAsync();
            Assert.Equal(HubConnectionState.Connected, hub.State);
            await hub.StopAsync();
        }
    }

    [SqlServerFact]
    public async Task Opening_a_connection_needs_the_users_csrf_token()
    {
        var browser = await SignedInBrowserAsync(Api());

        var refused = await Assert.ThrowsAsync<HttpRequestException>(() => browser.Hub(sendCsrfToken: false).StartAsync());

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [SqlServerFact]
    public async Task A_page_on_another_site_cannot_connect_even_with_the_users_cookie()
    {
        var browser = await SignedInBrowserAsync(Api());

        var negotiate = await Assert.ThrowsAsync<HttpRequestException>(() => browser.Hub(origin: "https://saldirgan.example").StartAsync());
        Assert.Equal(HttpStatusCode.Forbidden, negotiate.StatusCode);

        using (var request = new HttpRequestMessage(HttpMethod.Post, new Uri(LiveBrowser.HubPath + "/negotiate?negotiateVersion=1", UriKind.Relative)))
        {
            request.Headers.Add("Origin", "https://saldirgan.example");
            request.Headers.Add(AuthClient.CsrfHeader, browser.CsrfToken);
            using var response = await browser.Http.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var problem = await ProblemJson.ReadAsync(response);
            Assert.Equal("cross_origin", problem.GetProperty("code").GetString());
            Assert.Equal("Bu bağlantıya izin verilmiyor.", problem.GetProperty("title").GetString());
        }

        // Browsers apply no CORS to WebSockets; the origin check is what stops a direct one. "null" is refused too.
        foreach (var origin in new[] { "https://saldirgan.example", "https://localhost.saldirgan.example", "https://localhost:8443", "null" })
        {
            var refused = await Assert.ThrowsAnyAsync<Exception>(async () => await browser.ConnectWebSocketAsync(WebSocketUrl, origin, CancellationToken.None));
            Assert.Contains("403", refused.Message, StringComparison.Ordinal);
        }

        // The app's own pages may, whether served over https or by the development server over http.
        foreach (var origin in new[] { "https://localhost", "http://localhost", "https://LOCALHOST" })
        {
            using var socket = await browser.ConnectWebSocketAsync(WebSocketUrl, origin, CancellationToken.None);
            Assert.Equal(WebSocketState.Open, socket.State);
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        }
    }

    [SqlServerFact]
    public async Task A_client_can_call_nothing_on_the_hub()
    {
        var browser = await SignedInBrowserAsync(Api());
        var hub = browser.Hub();
        await hub.StartAsync();

        foreach (var method in new[] { "SendAsync", "OnConnectedAsync", "AssetCreated", "Dispose" })
        {
            await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync(method));
        }

        Assert.Equal(HubConnectionState.Connected, hub.State);
    }

    // --- Closed when the session ends -------------------------------------------------------------------------

    [SqlServerFact]
    public async Task Signing_out_closes_the_sessions_connections_at_once()
    {
        // The periodic check is far off: only the sign-out can close the connection within the test.
        var api = Api(TimeSpan.FromMinutes(5));
        var browser = await SignedInBrowserAsync(api);
        var webSocket = browser.Hub(HttpTransportType.WebSockets);
        var longPolling = browser.Hub(HttpTransportType.LongPolling);
        var closed = Task.WhenAll(webSocket.WhenClosed(), longPolling.WhenClosed());
        await webSocket.StartAsync();
        await longPolling.StartAsync();

        // Another user's connection stays open.
        var other = new ControllableDirectory();
        var otherBrowser = await SignedInBrowserAsync(Api(TimeSpan.FromMinutes(5), other), other);
        var otherHub = otherBrowser.Hub();
        await otherHub.StartAsync();

        using (var logout = await browser.Http.PostWithCsrfAsync(AuthClient.Logout, browser.CsrfToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        }

        await closed.WaitAsync(Patience);
        Assert.Equal(HubConnectionState.Connected, otherHub.State);

        // The old cookie cannot open a new one.
        var refused = await Assert.ThrowsAsync<HttpRequestException>(() => browser.Hub().StartAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    [SqlServerFact]
    public async Task An_open_connection_does_not_keep_an_idle_session_alive()
    {
        var browser = await SignedInBrowserAsync(Api());
        var signedInAt = (await SessionAsync()).LastSeenAt;

        // Well inside the idle timeout the page opens its connection: its requests and checks are no activity.
        _clock.Advance(TimeSpan.FromMinutes(15));
        var hub = browser.Hub();
        var closed = hub.WhenClosed();
        await hub.StartAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.Equal(HubConnectionState.Connected, hub.State);
        Assert.Equal(signedInAt, (await SessionAsync()).LastSeenAt);

        _clock.Advance(TimeSpan.FromMinutes(6));
        await closed.WaitAsync(Patience);

        var session = await SessionAsync();
        Assert.Equal(SessionEndReason.IdleTimeout, session.EndReason);
    }

    [SqlServerFact]
    public async Task A_connection_is_closed_when_the_directory_takes_the_users_access_away()
    {
        var browser = await SignedInBrowserAsync(Api());
        var hub = browser.Hub(HttpTransportType.WebSockets);
        var closed = hub.WhenClosed();
        await hub.StartAsync();

        _directory.Access = DirectoryAccessStatus.NotAuthorized;
        _clock.Advance(TimeSpan.FromMinutes(6));
        await closed.WaitAsync(Patience);

        var session = await SessionAsync();
        Assert.Equal(SessionEndReason.AccessRevoked, session.EndReason);

        // The revocation is audited by the system, with a correlation ID of its own although no request caused it.
        await using var context = database.CreateContext();
        var revoked = await context.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.EntityName == "AdminUser" && a.Action == AuditAction.AccessRevoked && a.EntityId == session.AdminUserId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("system", revoked.UserName);
        Assert.Matches("^[0-9a-f]{32}$", revoked.CorrelationId);
    }

    [SqlServerFact]
    public async Task A_short_directory_outage_does_not_close_the_connection()
    {
        var browser = await SignedInBrowserAsync(Api());
        var hub = browser.Hub();
        await hub.StartAsync();

        _directory.Access = DirectoryAccessStatus.DirectoryUnavailable;
        _clock.Advance(TimeSpan.FromMinutes(6));
        await WaitUntilAsync(() => _directory.AccessChecks > 0);
        await Task.Delay(TimeSpan.FromMilliseconds(600));

        Assert.Equal(HubConnectionState.Connected, hub.State);
        Assert.Null((await SessionAsync()).EndReason);
    }

    // --- Helpers ------------------------------------------------------------------------------------------------

    private static Uri WebSocketUrl => new("wss://localhost" + LiveBrowser.HubPath);

    private TestApiFactory Api(TimeSpan? sessionCheckInterval = null, ControllableDirectory? directory = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Realtime:SessionCheckInterval"] = (sessionCheckInterval ?? TimeSpan.FromMilliseconds(200)).ToString("c", System.Globalization.CultureInfo.InvariantCulture),
        };
        var api = new TestApiFactory(database.ConnectionString, useTestAuthentication: false, settings: settings, configureServices: services =>
        {
            services.AddSingleton<TimeProvider>(_clock);
            services.AddScoped<IDirectoryService>(_ => directory ?? _directory);
        });
        _resources.Add(api);
        return api;
    }

    private LiveBrowser Browser(TestApiFactory api)
    {
        if (!_resources.Contains(api))
        {
            _resources.Add(api);
        }

        var browser = new LiveBrowser(api);
        _resources.Add(browser);
        return browser;
    }

    private async Task<LiveBrowser> SignedInBrowserAsync(TestApiFactory api, ControllableDirectory? directory = null)
    {
        var browser = Browser(api);
        var user = directory ?? _directory;
        await browser.SignInAsync(user.UserName, ControllableDirectory.Password);
        return browser;
    }

    private async Task<UserSession> SessionAsync()
    {
        await using var context = database.CreateContext();
        return await context.UserSessions.AsNoTracking().SingleAsync(s => s.AdminUser.ObjectGuid == _directory.ObjectGuid);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition was not met in time.");
            await Task.Delay(50);
        }
    }
}
