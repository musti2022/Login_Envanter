using Microsoft.AspNetCore.WebUtilities;

namespace EnterpriseInventory.Api.Http;

internal static class ProblemDetailsSetup
{
    /// <summary>Framework default for 500, which differs from the reason phrase.</summary>
    private const string DefaultServerErrorTitle = "An error occurred while processing your request.";

    /// <summary>Turkish titles for the errors the framework produces itself; the UI shows them to the user.</summary>
    private static readonly Dictionary<int, string> TurkishTitles = new()
    {
        [StatusCodes.Status400BadRequest] = "İstek geçersiz.",
        [StatusCodes.Status401Unauthorized] = "Bu işlem için oturum açmanız gerekiyor.",
        [StatusCodes.Status403Forbidden] = "Bu işlem için yetkiniz yok.",
        [StatusCodes.Status404NotFound] = "İstenen kaynak bulunamadı.",
        [StatusCodes.Status405MethodNotAllowed] = "Bu işlem bu adreste desteklenmiyor.",
        [StatusCodes.Status409Conflict] = "İşlem, kaydın güncel durumuyla çakışıyor.",
        [StatusCodes.Status415UnsupportedMediaType] = "İstek içeriğinin biçimi desteklenmiyor.",
        [StatusCodes.Status429TooManyRequests] = "Çok fazla istek gönderildi. Lütfen biraz bekleyip tekrar deneyin.",
        [StatusCodes.Status500InternalServerError] = "Beklenmeyen bir hata oluştu.",
        [StatusCodes.Status503ServiceUnavailable] = "Hizmet şu anda kullanılamıyor.",
    };

    /// <summary>
    /// RFC 7807 error responses with a Turkish title and the request's correlation ID. Titles that an endpoint
    /// set itself are kept; only the framework's English defaults are replaced. Exception details are never
    /// included outside Development.
    /// </summary>
    public static IServiceCollection AddApiProblemDetails(this IServiceCollection services)
    {
        // A request the endpoint cannot bind (malformed JSON, "page=iki") is the client's error: 400 in every
        // environment. By default Development throws instead, which the exception handler turns into a 500.
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);

        return services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            if (problem.Status is { } status
                && TurkishTitles.TryGetValue(status, out var title)
                && (problem.Title is null || problem.Title == ReasonPhrases.GetReasonPhrase(status) || problem.Title == DefaultServerErrorTitle))
            {
                problem.Title = title;
            }

            // One ID to quote when reporting a problem: the correlation ID replaces the framework's trace ID.
            problem.Extensions.Remove("traceId");
            problem.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
        });
    }
}
