using System.Net;
using System.Text.RegularExpressions;
using EnterpriseInventory.IntegrationTests.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace EnterpriseInventory.IntegrationTests.Security;

/// <summary>
/// Access control for every endpoint the API maps, read from its routing table rather than listed by hand, so an
/// endpoint added later is covered without anyone remembering to add it here. No request reaches the database:
/// each is refused before its handler runs.
/// </summary>
public sealed partial class EndpointAccessTests(ITestOutputHelper output) : IAsyncLifetime, IDisposable
{
    /// <summary>The only endpoints a visitor may call: health probes, the CSRF token and sign-in.</summary>
    private static readonly string[] Anonymous = ["/api/auth/csrf", "/api/auth/login", "/api/health/live", "/api/health/ready"];

    private static readonly string[] AnyMethod = ["GET", "POST", "PUT", "DELETE"];

    private readonly TestApiFactory _api = new();
    private List<Route> _endpoints = [];

    public Task InitializeAsync()
    {
        _endpoints = _api.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            // An endpoint without method metadata (the hub's) answers any method, so every one is tried.
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? AnyMethod)
                .Select(method => (method, endpoint.RoutePattern.RawText ?? string.Empty)))
            .Distinct()
            .OrderBy(e => e.Item2, StringComparer.Ordinal)
            .ThenBy(e => e.method, StringComparer.Ordinal)
            .Select(e => new Route(e.method, e.Item2, new Uri(RouteParameter().Replace(e.Item2, "1"), UriKind.Relative)))
            .ToList();
        foreach (var (method, pattern, _) in _endpoints)
        {
            output.WriteLine($"{method} {pattern}");
        }

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _api.Dispose();

    [Fact]
    public void The_routing_table_holds_the_api_and_the_hub()
    {
        // A guard for the tests below: an empty or partial table would make them pass without checking anything.
        Assert.True(_endpoints.Count >= 40, $"Only {_endpoints.Count} method and route pairs were found.");
        Assert.Contains(_endpoints, e => e.Pattern.StartsWith("/api/assets", StringComparison.Ordinal));
        Assert.Contains(_endpoints, e => e.Pattern.StartsWith("/hubs/", StringComparison.Ordinal));
        Assert.Contains(_endpoints, e => e.Method == "DELETE");
    }

    [Fact]
    public async Task A_visitor_is_refused_everywhere_but_the_health_probes_and_sign_in()
    {
        using var client = _api.CreateAnonymousClient();
        var failures = new List<string>();

        foreach (var (method, pattern, uri) in _endpoints.Where(e => !Anonymous.Contains(e.Pattern)))
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), uri);
            using var response = await client.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                failures.Add($"{method} {pattern}: {(int)response.StatusCode}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public async Task A_signed_in_user_without_the_administrator_role_is_refused_everywhere_but_the_anonymous_endpoints()
    {
        using var client = _api.CreateSignedInClient("veli.kullanici", roles: "Reader");
        var failures = new List<string>();

        foreach (var (method, pattern, uri) in _endpoints.Where(e => !Anonymous.Contains(e.Pattern)))
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), uri);
            using var response = await client.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.Forbidden)
            {
                failures.Add($"{method} {pattern}: {(int)response.StatusCode}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public async Task Every_state_changing_endpoint_refuses_an_administrator_without_a_csrf_token()
    {
        using var client = _api.CreateSignedInClient();
        var failures = new List<string>();
        var checkedCount = 0;

        foreach (var (method, pattern, uri) in _endpoints.Where(e => !IsSafe(e.Method)))
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), uri) { Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json") };
            using var response = await client.SendAsync(request);
            checkedCount++;
            if (response.StatusCode != HttpStatusCode.BadRequest
                || (await ProblemJson.ReadAsync(response)).GetProperty("code").GetString() != "csrf_invalid")
            {
                failures.Add($"{method} {pattern}: {(int)response.StatusCode}");
            }
        }

        Assert.Empty(failures);
        Assert.True(checkedCount >= 10, $"Only {checkedCount} state-changing endpoints were checked.");
    }

    [Fact]
    public void Only_the_health_probes_csrf_token_and_sign_in_are_marked_anonymous()
    {
        var anonymous = _api.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .Distinct()
            .Order(StringComparer.Ordinal);

        Assert.Equal(Anonymous, anonymous);
        // Every anonymous endpoint but sign-in only reads.
        Assert.All(
            _endpoints.Where(e => Anonymous.Contains(e.Pattern) && e.Pattern != "/api/auth/login"),
            e => Assert.True(IsSafe(e.Method), $"{e.Method} {e.Pattern}"));
    }

    [Fact]
    public async Task The_health_probes_answer_only_reads()
    {
        using var client = _api.CreateAnonymousClient();
        using (var headRequest = new HttpRequestMessage(HttpMethod.Head, new Uri("/api/health/live", UriKind.Relative)))
        using (var head = await client.SendAsync(headRequest))
        {
            Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        }

        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
        {
            using var request = new HttpRequestMessage(method, new Uri("/api/health/live", UriKind.Relative));
            using var response = await client.SendAsync(request);
            Assert.False(response.IsSuccessStatusCode, $"{method} /api/health/live: {(int)response.StatusCode}");
        }
    }

    private sealed record Route(string Method, string Pattern, Uri Uri);

    private static bool IsSafe(string method) => HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

    /// <summary>A route parameter such as <c>{id:int}</c> or <c>{assetId}</c>, replaced by an ID that matches any constraint.</summary>
    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex RouteParameter();
}
