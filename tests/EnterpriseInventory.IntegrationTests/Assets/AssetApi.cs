using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnterpriseInventory.IntegrationTests.Api;

namespace EnterpriseInventory.IntegrationTests.Assets;

/// <summary>Calls <c>/api/assets</c> the way the web app does and reads the answers as raw JSON.</summary>
internal static class AssetApi
{
    public static Uri Uri(string pathAndQuery) => new(pathAndQuery, UriKind.Relative);

    /// <summary>GETs <paramref name="pathAndQuery"/>, asserts <c>200</c> and returns the JSON body.</summary>
    public static async Task<JsonElement> GetOkAsync(this HttpClient client, string pathAndQuery)
    {
        using var response = await client.GetAsync(Uri(pathAndQuery));
        return await ReadAsync(response, HttpStatusCode.OK);
    }

    /// <summary>Asserts the status (showing the body when it differs) and returns the JSON body.</summary>
    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {(int)expected}, got {(int)response.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>Sends a state-changing request with the client's CSRF token, as every POST, PUT and DELETE needs.</summary>
    public static async Task<HttpResponseMessage> SendWithCsrfAsync(this HttpClient client, HttpMethod method, string pathAndQuery, object? body = null)
    {
        var token = await client.GetCsrfTokenAsync();
        using var request = new HttpRequestMessage(method, Uri(pathAndQuery)) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add(AuthClient.CsrfHeader, token);
        return await client.SendAsync(request);
    }

    public static IEnumerable<string> Codes(this JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("assetCode").GetString()!);

    public static JsonElement Item(this JsonElement page, string assetCode) =>
        page.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("assetCode").GetString() == assetCode);

    /// <summary>The first message for <paramref name="field"/> in a 400 ValidationProblem.</summary>
    public static string FieldError(this JsonElement problem, string field) =>
        problem.GetProperty("errors").GetProperty(field)[0].GetString()!;
}
