using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EnterpriseInventory.IntegrationTests.Api;

/// <summary>The web app's side of signing in: every POST carries a CSRF token from <c>GET /api/auth/csrf</c>.</summary>
internal static class AuthClient
{
    public const string CsrfHeader = "X-CSRF-TOKEN";

    public static readonly Uri Csrf = new("/api/auth/csrf", UriKind.Relative);
    public static readonly Uri Me = new("/api/auth/me", UriKind.Relative);
    public static readonly Uri Logout = new("/api/auth/logout", UriKind.Relative);

    /// <summary>A CSRF token for whoever the client currently is (anonymous or signed in).</summary>
    public static async Task<string> GetCsrfTokenAsync(this HttpClient client)
    {
        using var response = await client.GetAsync(Csrf);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    /// <summary>Posts the sign-in form as the web app does: with a fresh anonymous CSRF token.</summary>
    public static async Task<HttpResponseMessage> PostLoginAsync(this HttpClient client, object body) =>
        await client.PostWithCsrfAsync(LoginEndpointTests.Login, await client.GetCsrfTokenAsync(), JsonContent.Create(body));

    /// <summary>Signs in and returns the CSRF token the sign-in response hands out for the new user.</summary>
    public static async Task<string> SignInAsync(this HttpClient client, string userName, string password)
    {
        using var response = await client.PostLoginAsync(new { userName, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("csrfToken").GetString()!;
    }

    public static async Task<HttpResponseMessage> PostWithCsrfAsync(this HttpClient client, Uri uri, string? csrfToken, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = content };
        if (csrfToken is not null)
        {
            request.Headers.Add(CsrfHeader, csrfToken);
        }

        return await client.SendAsync(request);
    }
}
