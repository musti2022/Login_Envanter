using EnterpriseInventory.Api.Http;

namespace EnterpriseInventory.Api.Realtime;

/// <summary>
/// Browsers do not apply CORS to WebSocket connections, so a page on another site could try to open one with the
/// user's cookies. Requests to the hubs that carry an <c>Origin</c> header are accepted only when it names the host the
/// request was sent to; the session cookie's SameSite=Strict already keeps the cookie off such requests, this is the
/// second line. Requests without the header (same-origin GETs, non-browser clients) still need a valid session.
/// </summary>
/// <remarks>
/// Only host and port are compared, not the scheme: in development the Vite server forwards https requests from an
/// http page without changing the Host header. A reverse proxy in front of the API must keep the original Host header.
/// </remarks>
internal sealed partial class SameOriginHubMiddleware(RequestDelegate next, ILogger<SameOriginHubMiddleware> logger)
{
    public const string HubsPath = "/hubs";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var origin = context.Request.Headers.Origin.ToString();
        if (context.Request.Path.StartsWithSegments(HubsPath) && origin.Length > 0 && !IsSameHost(origin, context.Request.Host))
        {
            LogRefused(context.Request.Path, origin);
            await ApiResults.Problem(
                    StatusCodes.Status403Forbidden,
                    "Bu bağlantıya izin verilmiyor.",
                    "Canlı bildirimlere yalnızca uygulamanın kendi sayfalarından bağlanılabilir.",
                    "cross_origin")
                .ExecuteAsync(context);
            return;
        }

        await next(context);
    }

    private static bool IsSameHost(string origin, HostString host)
    {
        // "null" (sandboxed frames, local files) and anything that is not an absolute http(s) URL are refused.
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        var samePort = host.Port is { } port ? port == uri.Port : uri.IsDefaultPort;
        return samePort && string.Equals(uri.Host, host.Host, StringComparison.OrdinalIgnoreCase);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Path} refused: the request came from another origin ({Origin})")]
    private partial void LogRefused(PathString path, string origin);
}
