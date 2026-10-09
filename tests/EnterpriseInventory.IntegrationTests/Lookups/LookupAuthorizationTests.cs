using System.Net;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Assets;

namespace EnterpriseInventory.IntegrationTests.Lookups;

/// <summary>Every lookup endpoint refuses visitors and signed-in users without the Administrator role, before touching the database.</summary>
public sealed class LookupAuthorizationTests
{
    public static TheoryData<string, string> Endpoints => new()
    {
        { "GET", "/api/brands" },
        { "POST", "/api/brands" },
        { "GET", "/api/models?brandId=1" },
        { "POST", "/api/models" },
        { "GET", "/api/cities" },
        { "POST", "/api/cities" },
        { "GET", "/api/locations?cityId=1" },
        { "POST", "/api/locations" },
        { "GET", "/api/departments" },
        { "POST", "/api/departments" },
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
