using System.Globalization;
using System.Security.Claims;
using EnterpriseInventory.Api.Http;
using EnterpriseInventory.Api.Security;
using EnterpriseInventory.Application.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace EnterpriseInventory.Api.Auth;

internal static class AuthEndpoints
{
    public const string LoginPath = "/api/auth/login";
    public const string CsrfPath = "/api/auth/csrf";

    /// <summary>Generous for a user name and password, small enough that the endpoint cannot be fed large bodies.</summary>
    private const long MaxLoginBodyBytes = 8 * 1024;

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Anonymous by necessity, rate limited per client address, and JSON only: a cross-site form cannot post
        // JSON without a CORS preflight, which this API never allows. Like every POST it needs a CSRF token.
        endpoints.MapPost(LoginPath, LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingSetup.LoginPolicy)
            .Accepts<LoginRequest>("application/json")
            .WithMetadata(new RequestSizeLimitAttribute(MaxLoginBodyBytes));

        // A CSRF token for the current user (or for an anonymous visitor, to sign in with). The token comes in the
        // response body and is sent back in the X-CSRF-TOKEN header; its cookie half is HttpOnly.
        endpoints.MapGet(CsrfPath, (HttpContext httpContext, IAntiforgery antiforgery) =>
                TypedResults.Ok(new CsrfToken(antiforgery.GetAndStoreTokens(httpContext).RequestToken!)))
            .AllowAnonymous();

        endpoints.MapGet("/api/auth/me", (HttpContext httpContext) => TypedResults.Ok(CurrentUser(httpContext.User)));

        endpoints.MapPost("/api/auth/logout", LogoutAsync);

        return endpoints;
    }

    private static async Task<Results<Ok<SignedInUser>, ValidationProblem, ProblemHttpResult>> LoginAsync(
        LoginRequest body, SignInHandler signIn, IAntiforgery antiforgery, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var result = await signIn.HandleAsync(new SignInRequest(body.UserName, body.Password), cancellationToken);
        return result.Outcome switch
        {
            SignInOutcome.Succeeded when result is { Account: { } account, Session: { } session } =>
                await SignInAsync(httpContext, antiforgery, account, session),
            SignInOutcome.ValidationFailed => ApiResults.ValidationProblem(result.Errors ?? new Dictionary<string, string[]>()),
            SignInOutcome.AccountUnavailable => Problem(
                StatusCodes.Status403Forbidden,
                "Hesabınızla şu anda giriş yapılamıyor.",
                "Hesabınız pasif veya süresi dolmuş olabilir ya da parolanızı değiştirmeniz gerekiyor. BT ekibiyle görüşün.",
                "account_unavailable"),
            SignInOutcome.NotAuthorized => Problem(
                StatusCodes.Status403Forbidden,
                "Bu uygulamaya giriş yetkiniz yok.",
                "Uygulamaya yalnızca Bim_Envanter grubunun üyeleri girebilir. Erişim için BT ekibiyle görüşün.",
                "not_authorized"),
            SignInOutcome.DirectoryUnavailable => Problem(
                StatusCodes.Status503ServiceUnavailable,
                "Giriş şu anda yapılamıyor.",
                "Kimlik doğrulama sunucusuna ulaşılamıyor. Lütfen biraz sonra tekrar deneyin.",
                "directory_unavailable"),
            SignInOutcome.SignInUnavailable => Problem(
                StatusCodes.Status503ServiceUnavailable,
                "Giriş şu anda yapılamıyor.",
                "Giriş kaydedilemedi. Lütfen biraz sonra tekrar deneyin.",
                "sign_in_unavailable"),

            // A locked account gets the same answer: AD reports lockout whatever password was typed, so saying so
            // would confirm that the account exists.
            _ => Problem(
                StatusCodes.Status401Unauthorized,
                "Kullanıcı adı veya parola hatalı.",
                "Birden fazla hatalı denemeden sonra hesabınız geçici olarak kilitlenebilir; sorun sürerse BT ekibiyle görüşün.",
                "invalid_credentials"),
        };
    }

    private static async Task<Results<Ok<SignedInUser>, ValidationProblem, ProblemHttpResult>> SignInAsync(
        HttpContext httpContext, IAntiforgery antiforgery, DirectoryAccount account, StartedSession session)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, account.ObjectGuid.ToString("D", CultureInfo.InvariantCulture)),
                new Claim(ClaimTypes.Name, account.SamAccountName),
                new Claim(AppClaimTypes.DisplayName, account.DisplayName),
                new Claim(ClaimTypes.PrimarySid, account.SecurityIdentifier),
                new Claim(ClaimTypes.Role, Roles.Administrator),
                new Claim(AppClaimTypes.SessionKey, session.Key),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme,
            ClaimTypes.Name,
            ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);

        // Not persistent: the browser drops the cookie when it closes. The ticket ends with the session.
        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = false, ExpiresUtc = session.ExpiresAt, AllowRefresh = false });

        // CSRF tokens are bound to the user, so the token used to sign in is no longer valid; hand out a new one.
        httpContext.User = principal;
        var csrfToken = antiforgery.GetAndStoreTokens(httpContext).RequestToken!;

        return TypedResults.Ok(CurrentUser(principal) with { CsrfToken = csrfToken });
    }

    private static async Task<NoContent> LogoutAsync(HttpContext httpContext, IUserSessionService sessions, CancellationToken cancellationToken)
    {
        if (httpContext.User.FindFirstValue(AppClaimTypes.SessionKey) is { } key)
        {
            await sessions.EndAsync(key, cancellationToken);
        }

        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return TypedResults.NoContent();
    }

    private static SignedInUser CurrentUser(ClaimsPrincipal user)
    {
        var userName = user.Identity?.Name ?? string.Empty;
        return new SignedInUser(
            userName,
            user.FindFirstValue(AppClaimTypes.DisplayName) ?? userName,
            [.. user.FindAll(ClaimTypes.Role).Select(role => role.Value)]);
    }

    private static ProblemHttpResult Problem(int status, string title, string? detail, string code) =>
        ApiResults.Problem(status, title, detail, code);
}

internal sealed record LoginRequest(string? UserName, string? Password);

/// <param name="CsrfToken">Only in the sign-in response: the CSRF token bound to the user who just signed in.</param>
internal sealed record SignedInUser(string UserName, string DisplayName, IReadOnlyList<string> Roles)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CsrfToken { get; init; }
}

internal sealed record CsrfToken(string Token);
