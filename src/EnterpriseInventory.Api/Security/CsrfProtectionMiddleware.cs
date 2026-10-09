using Microsoft.AspNetCore.Antiforgery;

namespace EnterpriseInventory.Api.Security;

/// <summary>
/// Requires a valid antiforgery token in the <see cref="HeaderName"/> header on every state-changing request
/// (anything but GET, HEAD, OPTIONS and TRACE), whatever the endpoint: protection does not depend on each
/// endpoint remembering to ask for it. The token is bound to the signed-in user, so a token obtained before
/// sign-in (or by another user) is refused. Clients get one from <c>GET /api/auth/csrf</c>.
/// </summary>
internal sealed partial class CsrfProtectionMiddleware(RequestDelegate next, IAntiforgery antiforgery, ILogger<CsrfProtectionMiddleware> logger)
{
    public const string HeaderName = "X-CSRF-TOKEN";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var method = context.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method))
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Method} {Path} refused: missing or invalid CSRF token ({Reason})")]
    private partial void LogRejected(string method, PathString path, string reason);
}
