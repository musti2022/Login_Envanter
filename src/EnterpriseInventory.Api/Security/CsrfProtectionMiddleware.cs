using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;

namespace EnterpriseInventory.Api.Security;

/// <summary>
/// Requires a valid antiforgery token in the <see cref="HeaderName"/> header on every state-changing request
/// (anything but GET, HEAD, OPTIONS and TRACE), whatever the endpoint: protection does not depend on each
/// endpoint remembering to ask for it. The token is bound to the signed-in user, so a token obtained before
/// sign-in (or by another user) is refused. Clients get one from <c>GET /api/auth/csrf</c>.
/// </summary>
/// <remarks>
/// Over HTTP/2 a browser opens a WebSocket with an extended CONNECT request (RFC 8441) instead of a GET with
/// <c>Upgrade: websocket</c>. It is the same handshake, cannot carry the header and is checked like the GET: the session,
/// and for the hubs the origin (<see cref="Realtime.SameOriginHubMiddleware"/>). Any other CONNECT needs the token.
/// </remarks>
internal sealed partial class CsrfProtectionMiddleware(RequestDelegate next, IAntiforgery antiforgery, ILogger<CsrfProtectionMiddleware> logger)
{
    public const string HeaderName = "X-CSRF-TOKEN";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var method = context.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method)
            || IsWebSocketHandshake(context))
        {
            await next(context);
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException ex)
        {
            LogRejected(context.Request.Method, context.Request.Path, ex.Message);
            await TypedResults.Problem(
                    "Sayfayı yenileyip işlemi tekrar deneyin.",
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Güvenlik doğrulaması başarısız oldu.",
                    extensions: new Dictionary<string, object?> { ["code"] = "csrf_invalid" })
                .ExecuteAsync(context);
            return;
        }

        await next(context);
    }

    private static bool IsWebSocketHandshake(HttpContext context) =>
        HttpMethods.IsConnect(context.Request.Method)
        && context.Features.Get<IHttpExtendedConnectFeature>() is { IsExtendedConnect: true, Protocol: { } protocol }
        && string.Equals(protocol, "websocket", StringComparison.OrdinalIgnoreCase);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Method} {Path} refused: missing or invalid CSRF token ({Reason})")]
    private partial void LogRejected(string method, PathString path, string reason);
}
