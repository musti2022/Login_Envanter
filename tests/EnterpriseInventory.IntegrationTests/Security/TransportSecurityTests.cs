using System.Net;
using EnterpriseInventory.IntegrationTests.Api;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EnterpriseInventory.IntegrationTests.Security;

/// <summary>
/// Outside Development the API tells browsers to use HTTPS only (HSTS) and sends plain HTTP requests to HTTPS.
/// On IIS the HTTPS port comes from the site binding; here it is set the same way the module sets it.
/// </summary>
public sealed class TransportSecurityTests
{
    private const string Host = "envanter.sirket.test";

    private static readonly Dictionary<string, string?> Production = new()
    {
        ["AllowedHosts"] = Host,
        ["https_port"] = "443",
    };

    [Fact]
    public async Task Https_answers_carry_hsts_outside_development()
    {
        await using var api = new TestApiFactory(environment: "Production", settings: Production);
        using var client = api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"https://{Host}"), AllowAutoRedirect = false });

        using var response = await client.GetAsync(new Uri("/api/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var hsts = Assert.Single(response.Headers.GetValues("Strict-Transport-Security"));
        Assert.StartsWith("max-age=", hsts, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Plain_http_is_sent_to_https()
    {
        await using var api = new TestApiFactory(environment: "Production", settings: Production);
        using var client = api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://{Host}"), AllowAutoRedirect = false });

        using var response = await client.GetAsync(new Uri("/api/assets?page=2", UriKind.Relative));

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal(new Uri($"https://{Host}/api/assets?page=2"), response.Headers.Location);
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Development_sends_no_hsts_so_a_developer_machine_is_not_pinned_to_https()
    {
        await using var api = new TestApiFactory();
        using var client = api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"https://{Host}"), AllowAutoRedirect = false });

        using var response = await client.GetAsync(new Uri("/api/health/live", UriKind.Relative));

        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }
}
