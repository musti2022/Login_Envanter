using System.Net;
using EnterpriseInventory.IntegrationTests.Assets;

namespace EnterpriseInventory.IntegrationTests.Api;

/// <summary>
/// The published site serves the React build from wwwroot on the API's origin: the page for every page address, the
/// built files with long caching, and nothing of the app under <c>/api</c> or <c>/hubs</c>.
/// </summary>
public sealed class WebAppHostingTests : IAsyncLifetime, IDisposable
{
    /// <summary>Written out so that a change to the policy is a deliberate one (and the React app is tried with it).</summary>
    private const string PagePolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; "
        + "connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    private const string Page = """<!doctype html><html lang="tr"><body><div id="root"></div><script type="module" src="/assets/index-AbC123.js"></script></body></html>""";

    private readonly string _webRoot = Directory.CreateTempSubdirectory("ei-webroot-").FullName;
    private TestApiFactory? _api;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        await File.WriteAllTextAsync(Path.Combine(_webRoot, "index.html"), Page);
        await File.WriteAllTextAsync(Path.Combine(_webRoot, "assets", "index-AbC123.js"), "console.log('envanter')");
        await File.WriteAllTextAsync(Path.Combine(_webRoot, "favicon.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        _api = new TestApiFactory(webRoot: _webRoot);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _api?.Dispose();
        Directory.Delete(_webRoot, recursive: true);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/envanter")]
    [InlineData("/envanter/12/duzenle")]
    [InlineData("/raporlar?groupBy=model")]
    [InlineData("/giris")]
    public async Task Every_page_address_gets_the_app_page_without_signing_in(string path)
    {
        using var client = _api!.CreateAnonymousClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Page, await response.Content.ReadAsStringAsync());
        // A new deployment's page is picked up at once; the page and its scripts are confined to this site.
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
        Assert.Equal(PagePolicy, string.Join(", ", response.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("DENY", string.Join(", ", response.Headers.GetValues("X-Frame-Options")));
    }

    [Fact]
    public async Task Built_files_are_served_and_cached_for_good_because_their_names_change_with_their_content()
    {
        using var client = _api!.CreateAnonymousClient();

        using var script = await client.GetAsync(new Uri("/assets/index-AbC123.js", UriKind.Relative));
        using var icon = await client.GetAsync(new Uri("/favicon.svg", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        Assert.Equal("text/javascript", script.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=31536000, immutable", script.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.OK, icon.StatusCode);
        Assert.Equal("no-cache", icon.Headers.CacheControl?.ToString());
    }

    [Theory]
    [InlineData("/assets/missing-1.js")]
    [InlineData("/robots.txt")]
    public async Task A_missing_file_is_404_not_the_page(string path)
    {
        using var client = _api!.CreateAnonymousClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("id=\"root\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/does-not-exist")]
    [InlineData("/API/Assets/x/y")]
    [InlineData("/hubs/unknown")]
    [InlineData("//api/assets")]
    public async Task Api_and_hub_addresses_never_fall_back_to_the_page(string path)
    {
        using var visitor = _api!.CreateAnonymousClient();
        using var administrator = _api.CreateSignedInClient();
        // Absolute, since a relative "//api/..." would name a host called "api".
        var address = new Uri($"https://localhost{path}");

        using var anonymous = await visitor.GetAsync(address);
        using var signedIn = await administrator.GetAsync(address);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, signedIn.StatusCode);
        Assert.Equal("application/problem+json", signedIn.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task The_page_is_only_read(string method)
    {
        // With the CSRF token, so the request gets past the CSRF check to routing.
        using var client = _api!.CreateSignedInClient();

        using var response = await client.SendWithCsrfAsync(new HttpMethod(method), "/envanter");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("id=\"root\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_write_to_an_unknown_api_address_is_still_the_apis_404()
    {
        using var client = _api!.CreateSignedInClient();

        using var response = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_apis_answers_to_a_wrong_method_or_body_are_kept()
    {
        // A catch-all page route took part in routing's method and content-type checks and turned these into 404s.
        using var client = _api!.CreateSignedInClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["assetCode"] = "DMR-FORM" });
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/assets", UriKind.Relative)) { Content = form };
        request.Headers.Add(AuthClient.CsrfHeader, await client.GetCsrfTokenAsync());

        using var wrongMethod = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/audit-logs");
        using var wrongBody = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongBody.StatusCode);
    }

    [Fact]
    public async Task A_head_request_gets_the_page_headers()
    {
        using var client = _api!.CreateAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Head, new Uri("/envanter", UriKind.Relative));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Without_a_build_a_page_address_is_an_unknown_address_and_the_api_is_unchanged()
    {
        // Development: Vite serves the app, the API has no wwwroot.
        await using var api = new TestApiFactory();
        using var client = api.CreateAnonymousClient();

        using var page = await client.GetAsync(new Uri("/envanter", UriKind.Relative));
        using var apiCall = await client.GetAsync(new Uri("/api/assets", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, page.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, apiCall.StatusCode);
    }
}
