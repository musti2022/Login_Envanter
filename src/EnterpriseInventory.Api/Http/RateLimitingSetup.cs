using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace EnterpriseInventory.Api.Http;

/// <summary>Limits for the global rate limiter, from the <c>RateLimiting</c> configuration section.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Requests allowed per user (or per client address before sign-in) in each window.</summary>
    [Range(1, 100_000)]
    public int PermitLimit { get; set; } = 300;

    [Range(1, 3600)]
    public int WindowSeconds { get; set; } = 60;
}

internal static class RateLimitingSetup
{
    /// <summary>
    /// A fixed-window limit per signed-in user, or per client address for anonymous requests. Rejected requests
    /// get 429 with a <c>Retry-After</c> header. The stricter login limit is added with the login (day 7).
    /// </summary>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var limits = context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.PermitLimit,
                    Window = TimeSpan.FromSeconds(limits.WindowSeconds),
                    QueueLimit = 0,
                });
            });

            // No body here: the status code pages middleware writes the problem details.
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };
        });

        return services;
    }

    private static string PartitionKey(HttpContext context) =>
        context.User.Identity is { IsAuthenticated: true, Name: { Length: > 0 } name }
            ? $"user:{name}"
            : $"address:{context.Connection.RemoteIpAddress}";
}
