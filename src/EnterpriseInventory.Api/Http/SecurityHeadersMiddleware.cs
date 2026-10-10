namespace EnterpriseInventory.Api.Http;

/// <summary>
/// Adds browser security headers to every response. API responses (<c>/api</c>, <c>/hubs</c>) also get a
/// Content-Security-Policy that allows nothing and are never cached, since they may contain inventory data. The
/// React app's pages and files get the app's policy (<see cref="WebAppHosting.ContentSecurityPolicy"/>).
/// </summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

            if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/hubs"))
            {
                headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
                headers.CacheControl = "no-store";
            }
            else
            {
                headers.ContentSecurityPolicy = WebAppHosting.ContentSecurityPolicy;
            }

            return Task.CompletedTask;
        });

        return next(context);
    }
}
