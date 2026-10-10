namespace EnterpriseInventory.Api.Http;

/// <summary>
/// Serves the React app's production build from <c>wwwroot</c>, on the same origin as <c>/api</c> and <c>/hubs</c>,
/// so there is no CORS and the cookies stay same-site. In development Vite serves the app and <c>wwwroot</c> is absent;
/// then a page address is an unknown address like any other and the API works as before.
/// </summary>
internal static class WebAppHosting
{
    /// <summary>
    /// The pages' policy: their own scripts, styles, images and connections only. MUI writes its styles into style
    /// elements at run time, which needs <c>'unsafe-inline'</c> for styles (not for scripts).
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; "
        + "connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    private const string Page = "/index.html";

    /// <summary>Vite names built files after their content (<c>/assets/index-CgXkV-bW.js</c>), so they never change.</summary>
    private const string ImmutableCache = "public, max-age=31536000, immutable";

    /// <summary>
    /// The built files, and <c>index.html</c> for any other page address (<c>/</c>, <c>/envanter/12</c>) so the app's
    /// router takes over. Before authentication: the files are the same for everyone and hold no data; the app then
    /// asks <c>/api</c>, which checks. Only a read that matched no endpoint gets the page; addresses under <c>/api</c>
    /// and <c>/hubs</c>, and file names, are left alone, so an unknown API address is still a 401 or 404 and a write to
    /// a page address a 404. Not a catch-all route (<c>MapFallbackToFile</c>): routing applies its method and
    /// content-type checks before route constraints, so a catch-all turned the API's 405 and 415 answers into 404s.
    /// </summary>
    public static IApplicationBuilder UseWebApp(this IApplicationBuilder app)
    {
        var webRoot = app.ApplicationServices.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider;
        return app
            .Use((context, next) =>
            {
                if (context.GetEndpoint() is null
                    && (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
                    && IsPageAddress(context.Request.Path)
                    && webRoot.GetFileInfo(Page).Exists)
                {
                    context.Request.Path = Page;
                }

                return next(context);
            })
            .UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = file => file.Context.Response.Headers.CacheControl =
                    file.Context.Request.Path.StartsWithSegments("/assets") ? ImmutableCache : "no-cache",
            });
    }

    /// <summary>Everything but the <c>api</c> and <c>hubs</c> segments and names with an extension.</summary>
    internal static bool IsPageAddress(PathString path)
    {
        // "//api/assets" is not an API address for routing, but it must not get the page either.
        var trimmed = (path.Value ?? string.Empty).TrimStart('/');
        var first = trimmed.Split('/', 2)[0];
        var last = trimmed[(trimmed.LastIndexOf('/') + 1)..];
        return !first.Equals("api", StringComparison.OrdinalIgnoreCase)
            && !first.Equals("hubs", StringComparison.OrdinalIgnoreCase)
            && !last.Contains('.', StringComparison.Ordinal);
    }
}
