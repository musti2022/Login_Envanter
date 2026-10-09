using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EnterpriseInventory.IntegrationTests.Api;

/// <summary>Behaviour every API response shares: access control, errors, correlation, headers and limits.</summary>
public class ApiPipelineTests
{
    private const string CorrelationHeader = "X-Correlation-ID";

    [Fact]
    public async Task Unknown_routes_require_sign_in_before_revealing_anything()
    {
        await using var api = new TestApiFactory();
        using var client = api.CreateAnonymousClient();

        using var response = await client.GetAsync(new Uri("/api/does-not-exist", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await ProblemJson.ReadAsync(response);
    }

    [Fact]
    public async Task Errors_have_a_turkish_title_and_the_correlation_id_of_the_response()
    {
        await using var api = new TestApiFactory();
        using var client = api.CreateSignedInClient();

        using var response = await client.GetAsync(new Uri("/api/does-not-exist", UriKind.Relative));
        var problem = await ProblemJson.ReadAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("İstenen kaynak bulunamadı.", problem.GetProperty("title").GetString());
        Assert.Equal(response.Headers.GetValues(CorrelationHeader).Single(), problem.GetProperty("correlationId").GetString());
        Assert.False(problem.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task Unhandled_exceptions_return_500_without_exception_details()
    {
        await using var api = new TestApiFactory(
            environment: "Staging",
            appendToPipeline: app => app.Run(_ => throw new InvalidOperationException("Server=db01;Password=secret")));
        using var client = api.CreateSignedInClient();

        using var response = await client.GetAsync(new Uri("/api/fails", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();
        var problem = await ProblemJson.ReadAsync(response);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("Beklenmeyen bir hata oluştu.", problem.GetProperty("title").GetString());
        Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.Equal(response.Headers.GetValues(CorrelationHeader).Single(), problem.GetProperty("correlationId").GetString());
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public async Task A_request_body_that_cannot_be_read_is_400_in_every_environment(string environment)
    {
        await using var api = new TestApiFactory(environment: environment);
        using var client = api.CreateAnonymousClient();
        using var content = new StringContent("{ \"userName\": ", System.Text.Encoding.UTF8, "application/json");

        using var response = await client.PostWithCsrfAsync(LoginEndpointTests.Login, await client.GetCsrfTokenAsync(), content);
        var problem = await ProblemJson.ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("İstek geçersiz.", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Correlation_ids_are_generated_by_the_server_for_every_request()
    {
        await using var api = new TestApiFactory();
        using var client = api.CreateAnonymousClient();
        using var forged = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/health/live", UriKind.Relative));
        forged.Headers.Add(CorrelationHeader, "forged-id");

        using var first = await client.SendAsync(forged);
        using var second = await client.GetAsync(new Uri("/api/health/live", UriKind.Relative));

        var firstId = first.Headers.GetValues(CorrelationHeader).Single();
        var secondId = second.Headers.GetValues(CorrelationHeader).Single();
        Assert.Matches("^[0-9a-f]{32}$", firstId);
        Assert.Matches("^[0-9a-f]{32}$", secondId);
        Assert.NotEqual(firstId, secondId);
    }

    [Fact]
    public async Task Api_responses_carry_security_headers_and_are_never_cached()
    {
        await using var api = new TestApiFactory();
        using var client = api.CreateAnonymousClient();

        using var response = await client.GetAsync(new Uri("/api/health/live", UriKind.Relative));

        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", Header(response, "Content-Security-Policy"));
        Assert.Equal("no-store", Header(response, "Cache-Control"));
    }

    [Fact]
    public async Task Cross_origin_requests_get_no_cors_permission()
    {
        await using var api = new TestApiFactory();
        using var client = api.CreateAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/health/live", UriKind.Relative));
        request.Headers.Add("Origin", "https://attacker.example");
        using var preflight = new HttpRequestMessage(HttpMethod.Options, new Uri("/api/health", UriKind.Relative));
        preflight.Headers.Add("Origin", "https://attacker.example");
        preflight.Headers.Add("Access-Control-Request-Method", "GET");

        using var response = await client.SendAsync(request);
        using var preflightResponse = await client.SendAsync(preflight);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(preflightResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Requests_over_the_limit_get_429_with_retry_after()
    {
        await using var api = new TestApiFactory(settings: new Dictionary<string, string?>
        {
            ["RateLimiting:PermitLimit"] = "2",
            ["RateLimiting:WindowSeconds"] = "60",
        });
        using var client = api.CreateAnonymousClient();
        var live = new Uri("/api/health/live", UriKind.Relative);

        (await client.GetAsync(live)).Dispose();
        (await client.GetAsync(live)).Dispose();
        using var limited = await client.GetAsync(live);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.InRange(int.Parse(Header(limited, "Retry-After"), System.Globalization.CultureInfo.InvariantCulture), 1, 60);
        Assert.Equal(
            "Çok fazla istek gönderildi. Lütfen biraz bekleyip tekrar deneyin.",
            (await ProblemJson.ReadAsync(limited)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Each_signed_in_user_has_a_separate_limit()
    {
        await using var api = new TestApiFactory(settings: new Dictionary<string, string?> { ["RateLimiting:PermitLimit"] = "1" });
        using var ayse = api.CreateSignedInClient("ayse.admin");
        using var mehmet = api.CreateSignedInClient("mehmet.admin");
        var live = new Uri("/api/health/live", UriKind.Relative);

        (await ayse.GetAsync(live)).Dispose();
        using var ayseLimited = await ayse.GetAsync(live);
        using var mehmetAllowed = await mehmet.GetAsync(live);

        Assert.Equal(HttpStatusCode.TooManyRequests, ayseLimited.StatusCode);
        Assert.Equal(HttpStatusCode.OK, mehmetAllowed.StatusCode);
    }

    [Fact]
    public async Task Invalid_rate_limit_settings_stop_the_application_from_starting()
    {
        await using var api = new TestApiFactory(settings: new Dictionary<string, string?> { ["RateLimiting:PermitLimit"] = "0" });

        Assert.Throws<OptionsValidationException>(() => api.CreateAnonymousClient());
    }

    [Theory]
    [InlineData("0", "8")]
    [InlineData("20", "48")]
    [InlineData("120", "1")] // idle timeout longer than the absolute one
    public async Task Invalid_session_settings_stop_the_application_from_starting(string idleMinutes, string absoluteHours)
    {
        await using var api = new TestApiFactory(settings: new Dictionary<string, string?>
        {
            ["Session:IdleTimeoutMinutes"] = idleMinutes,
            ["Session:AbsoluteTimeoutHours"] = absoluteHours,
        });

        Assert.Throws<OptionsValidationException>(() => api.CreateAnonymousClient());
    }

    [Theory]
    [InlineData("Production", "")]
    [InlineData("Staging", "")]
    [InlineData("Production", "relative/keys")]
    [InlineData("Production", "/srv/CHANGE-ME/keys")]
    public async Task Without_a_safe_place_for_the_cookie_keys_the_application_does_not_start(string environment, string keysDirectory)
    {
        await using var api = new TestApiFactory(environment: environment, settings: new Dictionary<string, string?> { ["DataProtection:KeysDirectory"] = keysDirectory });

        var error = Assert.Throws<OptionsValidationException>(() => api.CreateAnonymousClient());
        Assert.Contains("DataProtection:KeysDirectory", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_the_health_probes_csrf_token_and_sign_in_allow_anonymous_access()
    {
        await using var api = new TestApiFactory();
        using var client = api.CreateAnonymousClient();

        var anonymous = api.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() is not null)
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .Order(StringComparer.Ordinal);

        Assert.Equal(["/api/auth/csrf", "/api/auth/login", "/api/health/live", "/api/health/ready"], anonymous);
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values)
            ? string.Join(", ", values)
            : throw new Xunit.Sdk.XunitException($"Header {name} is missing.");
}
