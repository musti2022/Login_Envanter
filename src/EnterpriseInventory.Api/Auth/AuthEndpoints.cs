using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using EnterpriseInventory.Api.Http;
using EnterpriseInventory.Api.Security;
using EnterpriseInventory.Application.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace EnterpriseInventory.Api.Auth;

internal static class AuthEndpoints
{
    public const string LoginPath = "/api/auth/login";

    /// <summary>Generous for a user name and password, small enough that the endpoint cannot be fed large bodies.</summary>
    private const long MaxLoginBodyBytes = 8 * 1024;

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Anonymous by necessity, rate limited per client address, and JSON only: a cross-site form cannot post
        // JSON without a CORS preflight, which this API never allows.
        endpoints.MapPost(LoginPath, LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingSetup.LoginPolicy)
            .Accepts<LoginRequest>("application/json")
            .WithMetadata(new RequestSizeLimitAttribute(MaxLoginBodyBytes));

        return endpoints;
    }

    private static async Task<Results<Ok<SignedInUser>, ValidationProblem, ProblemHttpResult>> LoginAsync(
        LoginRequest body, SignInHandler signIn, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var result = await signIn.HandleAsync(new SignInRequest(body.UserName, body.Password), cancellationToken);
        return result.Outcome switch
        {
            SignInOutcome.Succeeded when result.Account is { } account => await SignInAsync(httpContext, account),
            SignInOutcome.ValidationFailed => TypedResults.ValidationProblem(
                CamelCaseKeys(result.Errors ?? new Dictionary<string, string[]>()), title: "İstek geçersiz."),
            SignInOutcome.AccountUnavailable => Problem(
                StatusCodes.Status403Forbidden,
                "Hesabınızla şu anda giriş yapılamıyor.",
                "Hesabınız pasif, kilitli veya süresi dolmuş olabilir ya da parolanızı değiştirmeniz gerekiyor. BT ekibiyle görüşün.",
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
            _ => Problem(
                StatusCodes.Status401Unauthorized,
                "Kullanıcı adı veya parola hatalı.",
                null,
                "invalid_credentials"),
        };
    }

    private static async Task<Results<Ok<SignedInUser>, ValidationProblem, ProblemHttpResult>> SignInAsync(
        HttpContext httpContext, DirectoryAccount account)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, account.ObjectGuid.ToString("D", CultureInfo.InvariantCulture)),
                new Claim(ClaimTypes.Name, account.SamAccountName),
                new Claim(AppClaimTypes.DisplayName, account.DisplayName),
                new Claim(ClaimTypes.PrimarySid, account.SecurityIdentifier),
                new Claim(ClaimTypes.Role, Roles.Administrator),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme,
            ClaimTypes.Name,
            ClaimTypes.Role);

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = false });

        return TypedResults.Ok(new SignedInUser(account.SamAccountName, account.DisplayName, [Roles.Administrator]));
    }

    private static ProblemHttpResult Problem(int status, string title, string? detail, string code) =>
        TypedResults.Problem(detail, statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });

    private static Dictionary<string, string[]> CamelCaseKeys(IDictionary<string, string[]> errors) =>
        errors.ToDictionary(e => JsonNamingPolicy.CamelCase.ConvertName(e.Key), e => e.Value);
}

internal sealed record LoginRequest(string? UserName, string? Password);

internal sealed record SignedInUser(string UserName, string DisplayName, IReadOnlyList<string> Roles);
