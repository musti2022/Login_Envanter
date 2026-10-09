using System.Net;
using EnterpriseInventory.IntegrationTests.ActiveDirectory;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Assets;

namespace EnterpriseInventory.IntegrationTests.Employees;

/// <summary><c>GET /api/employees/search</c>: finding the person to give an asset to (day 21).</summary>
public sealed class EmployeeSearchApiTests
{
    [Fact]
    public async Task Visitors_get_401_and_users_without_the_administrator_role_get_403()
    {
        await using var api = new TestApiFactory();
        using var visitor = api.CreateAnonymousClient();
        using var reader = api.CreateSignedInClient("veli.user", roles: "Reader");

        using var anonymous = await visitor.GetAsync(AssetApi.Uri("/api/employees/search?q=mehmet"));
        using var forbidden = await reader.GetAsync(AssetApi.Uri("/api/employees/search?q=mehmet"));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [ActiveDirectoryFact]
    public async Task An_administrator_finds_a_directory_user_who_is_not_a_member_of_the_allowed_group()
    {
        await using var api = new TestApiFactory(settings: TestActiveDirectory.Settings());
        using var client = api.CreateSignedInClient();

        var found = await client.GetOkAsync("/api/employees/search?q=mehmet%20%C3%B6z");

        var mehmet = Assert.Single(found.GetProperty("items").EnumerateArray());
        Assert.Equal("mehmet.user", mehmet.GetProperty("userName").GetString());
        Assert.Equal("Mehmet Öztürk", mehmet.GetProperty("displayName").GetString());
        Assert.True(Guid.TryParse(mehmet.GetProperty("objectGuid").GetString(), out _));
        Assert.False(found.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task The_development_fake_directory_answers_too()
    {
        await using var api = new TestApiFactory(settings: new Dictionary<string, string?>
        {
            ["ActiveDirectory:Mode"] = "Fake",
            ["ActiveDirectory:FakeUsers:0:UserName"] = "dev.calisan",
            ["ActiveDirectory:FakeUsers:0:Password"] = "dev-only-password",
            ["ActiveDirectory:FakeUsers:0:DisplayName"] = "Deniz Işık",
            ["ActiveDirectory:FakeUsers:0:IsAllowedGroupMember"] = "false",
        });
        using var client = api.CreateSignedInClient();

        var found = await client.GetOkAsync("/api/employees/search?q=deniz");

        Assert.Equal("dev.calisan", Assert.Single(found.GetProperty("items").EnumerateArray()).GetProperty("userName").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("?q=m")]
    [InlineData("?q=a%20b%20c%20d%20e")]
    public async Task A_search_too_short_or_too_long_is_refused_with_a_turkish_message(string query)
    {
        await using var api = new TestApiFactory();
        using var client = api.CreateSignedInClient();

        using var response = await client.GetAsync(AssetApi.Uri($"/api/employees/search{query}"));

        var problem = await AssetApi.ReadAsync(response, HttpStatusCode.BadRequest);
        Assert.False(string.IsNullOrEmpty(problem.FieldError("q")));
    }

    [Fact]
    public async Task An_unreachable_directory_gives_503()
    {
        await using var api = new TestApiFactory();
        using var client = api.CreateSignedInClient();

        using var response = await client.GetAsync(AssetApi.Uri("/api/employees/search?q=mehmet"));

        var problem = await AssetApi.ReadAsync(response, HttpStatusCode.ServiceUnavailable);
        Assert.Equal("directory_unavailable", problem.GetProperty("code").GetString());
    }
}
