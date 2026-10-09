using System.Net.Http.Json;
using System.Text.Json;

namespace EnterpriseInventory.IntegrationTests.Api;

internal static class ProblemJson
{
    /// <summary>Asserts an RFC 7807 response and returns its JSON body.</summary>
    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
