using EnterpriseInventory.Application.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace EnterpriseInventory.Api.Security;

internal static class SecurityServiceCollectionExtensions
{
    public const string SessionCookieName = "__Host-EnterpriseInventory";
    public const string CsrfCookieName = "__Host-EnterpriseInventory.Csrf";

    /// <summary>
    /// Cookie authentication backed by server-side sessions, CSRF protection and deny-by-default authorization:
    /// an endpoint without its own policy, and any request that matches no endpoint, requires a signed-in
    /// Administrator. Anonymous endpoints must opt out explicitly with <c>AllowAnonymous</c>.
    /// </summary>
    /// <remarks>
    /// The cookie only carries the session key and the user's directory identity, encrypted with Data Protection.
    /// Every request is checked against the session (<see cref="SessionCookieEvents"/>): the idle timeout, the
    /// absolute timeout, sign-out and the periodic directory re-check are all enforced on the server.
    /// </remarks>
    public static IServiceCollection AddApiSecurity(this IServiceCollection services)
    {
        services.AddOptions<UserSessionOptions>()
            .BindConfiguration(UserSessionOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<SessionCookieEvents>();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                // __Host- cookies are only accepted over HTTPS, without a Domain and for Path=/.
                options.Cookie.Name = SessionCookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.Path = "/";

                // The ticket never outlives the session's absolute timeout and is never renewed; the idle timeout
                // is enforced by the server-side session.
                options.SlidingExpiration = false;
                options.EventsType = typeof(SessionCookieEvents);
            });
        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<IOptions<UserSessionOptions>, TimeProvider>((cookie, session, timeProvider) =>
            {
                cookie.ExpireTimeSpan = session.Value.Timeouts.Absolute;
                cookie.TimeProvider = timeProvider;
            });

        services.AddAntiforgery(options =>
        {
            options.HeaderName = CsrfProtectionMiddleware.HeaderName;
            options.Cookie.Name = CsrfCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";

            // SecurityHeadersMiddleware already forbids framing for every response.
            options.SuppressXFrameOptionsHeader = true;
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
}
