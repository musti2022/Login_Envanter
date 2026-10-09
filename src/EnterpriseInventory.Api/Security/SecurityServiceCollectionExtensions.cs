using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

namespace EnterpriseInventory.Api.Security;

internal static class SecurityServiceCollectionExtensions
{
    /// <summary>
    /// Cookie authentication and deny-by-default authorization: an endpoint without its own policy, and any
    /// request that matches no endpoint, requires a signed-in Administrator. Anonymous endpoints must opt out
    /// explicitly with <c>AllowAnonymous</c>.
    /// </summary>
    /// <remarks>
    /// The cookie is issued by <c>POST /api/auth/login</c> only to members of the allowed group. Server-side
    /// sessions, logout and CSRF protection are added on day 9.
    /// </remarks>
    public static IServiceCollection AddApiSecurity(this IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                // __Host- cookies are only accepted over HTTPS, without a Domain and for Path=/.
                options.Cookie.Name = "__Host-EnterpriseInventory";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.Path = "/";

                // Signed out after 20 minutes without a request.
                options.ExpireTimeSpan = TimeSpan.FromMinutes(20);
                options.SlidingExpiration = true;

                // An API answers with status codes instead of redirecting to a login page.
                options.Events.OnRedirectToLogin = context => SetStatus(context.Response, StatusCodes.Status401Unauthorized);
                options.Events.OnRedirectToAccessDenied = context => SetStatus(context.Response, StatusCodes.Status403Forbidden);
            });

        var administrator = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireRole(Roles.Administrator)
            .Build();
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.Administrator, administrator)
            .SetDefaultPolicy(administrator)
            .SetFallbackPolicy(administrator);

        return services;
    }

    private static Task SetStatus(HttpResponse response, int statusCode)
    {
        response.StatusCode = statusCode;
        return Task.CompletedTask;
    }
}
