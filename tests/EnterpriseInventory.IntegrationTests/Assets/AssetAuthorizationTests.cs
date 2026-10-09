using System.Net;
using EnterpriseInventory.IntegrationTests.Api;

namespace EnterpriseInventory.IntegrationTests.Assets;

/// <summary>Every asset endpoint refuses visitors and signed-in users without the Administrator role, before touching the database.</summary>
public sealed class AssetAuthorizationTests
{
    public static TheoryData<string, string> Endpoints => new()
    {
        { "GET", "/api/assets" },
        { "GET", "/api/assets/1" },
        { "POST", "/api/assets" },
        { "PUT", "/api/assets/1" },
        { "DELETE", "/api/assets/1?rowVersion=AAAAAAAAB9A%3D" },
        { "GET", "/api/assets/1/history" },
        { "GET", "/api/assets/1/assignments" },
        { "POST", "/api/assets/1/assignments" },
        { "POST", "/api/assets/1/returns" },
        { "GET", "/api/employees/search?q=mehmet" },
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Visitors_get_401(string method, string path)
    {
        await using var api = new TestApiFactory();
        using var client = api.CreateAnonymousClient();

        using var request = new HttpRequestMessage(new HttpMethod(method), AssetApi.Uri(path));
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await ProblemJson.ReadAsync(response);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Users_without_the_administrator_role_get_403(string method, string path)
    {
        await using var api = new TestApiFactory();
        using var client = api.CreateSignedInClient("veli.user", roles: "Reader");

        using var request = new HttpRequestMessage(new HttpMethod(method), AssetApi.Uri(path));
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await ProblemJson.ReadAsync(response);
    }
}
