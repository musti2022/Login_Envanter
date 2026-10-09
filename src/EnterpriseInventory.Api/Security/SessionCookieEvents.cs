using System.Security.Claims;
using EnterpriseInventory.Application.Authentication;
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
        var result = key is null ? null : await sessions.ValidateAsync(key, context.HttpContext.RequestAborted);
        if (result is not { IsValid: true })
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(context.Scheme.Name);
        }
    }

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
