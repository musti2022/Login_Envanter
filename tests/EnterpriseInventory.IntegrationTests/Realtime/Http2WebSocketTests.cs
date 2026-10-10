using System.Net;
using System.Net.WebSockets;
using EnterpriseInventory.IntegrationTests.Api;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseInventory.IntegrationTests.Realtime;

/// <summary>
/// Over HTTP/2 a browser opens a WebSocket with an extended CONNECT request (RFC 8441), which Kestrel offers. Found on
/// day 38 with the published site in Chromium: the CSRF check took that handshake for a state-changing request and
/// refused it, so live updates fell back to slower transports. These run on real Kestrel; the in-memory test server
/// speaks only HTTP/1.1.
/// </summary>
public sealed class Http2WebSocketTests
{
    [Fact]
    public async Task A_websocket_over_http2_reaches_the_hub()
    {
        await using var api = StartOnHttp2(out var address);
        using var socket = new ClientWebSocket();
        socket.Options.HttpVersion = HttpVersion.Version20;
        socket.Options.HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.SetRequestHeader(TestAuthHandler.UserHeader, "ayse.admin");
        socket.Options.SetRequestHeader(TestAuthHandler.RolesHeader, "Administrator");
        using var invoker = new HttpMessageInvoker(new SocketsHttpHandler());

        await socket.ConnectAsync(new Uri($"ws://{address.Authority}/hubs/inventory"), invoker, CancellationToken.None);

        // 200 is HTTP/2's answer to an accepted extended CONNECT (HTTP/1.1 answers 101).
        Assert.Equal(HttpStatusCode.OK, socket.HttpStatusCode);
    }

    [Fact]
    public async Task A_websocket_over_http2_from_another_site_is_still_refused()
    {
        await using var api = StartOnHttp2(out var address);
        using var socket = new ClientWebSocket();
        socket.Options.HttpVersion = HttpVersion.Version20;
        socket.Options.HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.SetRequestHeader(TestAuthHandler.UserHeader, "ayse.admin");
        socket.Options.SetRequestHeader(TestAuthHandler.RolesHeader, "Administrator");
        socket.Options.SetRequestHeader("Origin", "https://baska-site.example");
        using var invoker = new HttpMessageInvoker(new SocketsHttpHandler());

        await Assert.ThrowsAsync<WebSocketException>(() =>
            socket.ConnectAsync(new Uri($"ws://{address.Authority}/hubs/inventory"), invoker, CancellationToken.None));
        Assert.Equal(HttpStatusCode.Forbidden, socket.HttpStatusCode);
    }

    [Fact]
    public async Task A_connect_request_that_is_not_a_websocket_needs_the_csrf_token()
    {
        await using var api = new TestApiFactory();
        _ = api.Server;

        var context = await api.Server.SendAsync(request =>
        {
            request.Request.Method = HttpMethods.Connect;
            request.Request.Scheme = "https";
            request.Request.Path = "/hubs/inventory";
            request.Request.Headers[TestAuthHandler.UserHeader] = "ayse.admin";
            request.Request.Headers[TestAuthHandler.RolesHeader] = "Administrator";
        });

        using var body = new StreamReader(context.Response.Body);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Contains("csrf_invalid", await body.ReadToEndAsync(), StringComparison.Ordinal);
    }

    private static TestApiFactory StartOnHttp2(out Uri address)
    {
        var api = new TestApiFactory();
        // Cleartext HTTP/2 (prior knowledge); the client asks for exactly HTTP/2.
        api.UseKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2));
        api.StartServer();
        address = new Uri(api.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        return api;
    }
}
