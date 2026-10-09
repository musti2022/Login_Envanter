using System.Security.Claims;
using EnterpriseInventory.Application.Authentication;
using EnterpriseInventory.Api.Realtime;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace EnterpriseInventory.Api.Security;

/// <summary>
/// Ties the authentication cookie to its server-side session: on every request the session must still be valid
/// (see <see cref="IUserSessionService.ValidateAsync"/>), otherwise the cookie is rejected and deleted. A cookie
/// without a session key, such as one issued before sessions existed, is rejected too.
/// </summary>
internal sealed class SessionCookieEvents(IUserSessionService sessions) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var key = context.Principal?.FindFirstValue(AppClaimTypes.SessionKey);
        var cancellationToken = context.HttpContext.RequestAborted;
        var result = key is null ? null
            : IsActivity(context.Request) ? await sessions.ValidateAsync(key, cancellationToken)
            : await sessions.CheckAsync(key, cancellationToken);
        if (result is not { IsValid: true })
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(context.Scheme.Name);
        }
    }

    /// <summary>
    /// Marks a read the user did not ask for, such as a screen refreshing itself after a live notification. Only
    /// reads can be marked; the header can only shorten a session, never extend one.
    /// </summary>
    public const string BackgroundHeaderName = "X-Background-Request";

    /// <summary>
    /// Whether the request is the user's own doing. The live connection's traffic and background reads are not: an
    /// open page must not keep an idle session alive.
    /// </summary>
    private static bool IsActivity(HttpRequest request) =>
        !request.Path.StartsWithSegments(SameOriginHubMiddleware.HubsPath)
        && !((HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)) && request.Headers[BackgroundHeaderName] == "1");

    // An API answers with status codes instead of redirecting to a login page.
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context) =>
        SetStatus(context, StatusCodes.Status401Unauthorized);

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context) =>
        SetStatus(context, StatusCodes.Status403Forbidden);

    private static Task SetStatus(RedirectContext<CookieAuthenticationOptions> context, int statusCode)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.StatusCode = statusCode;
        return Task.CompletedTask;
    }
}
