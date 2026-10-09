using System.Net;
using System.Text.Json;

namespace EnterpriseInventory.IntegrationTests.Assets;

/// <summary>
/// Employees of the Development fake directory for assignment tests, with names of their own so tests sharing a
/// database never share an employee record by accident; and the calls the assignment screen makes.
/// </summary>
internal sealed class FakeEmployees
{
    private const string Password = "fake-directory-password";

    public FakeEmployees(int count = 3)
    {
        var run = Guid.NewGuid().ToString("N")[..8];
        Names = [.. Enumerable.Range(1, count).Select(i => $"z{run}.{i}")];
        Disabled = $"z{run}.pasif";
    }

    /// <summary>Enabled people who are not members of the allowed group.</summary>
    public IReadOnlyList<string> Names { get; }

    public string Disabled { get; }

    public string First => Names[0];

    /// <summary>The API's settings: the fake directory with these people (and a signed-in admin who never signs in).</summary>
    public Dictionary<string, string?> Settings(Dictionary<string, string?>? more = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ActiveDirectory:Mode"] = "Fake",
            ["RateLimiting:PermitLimit"] = "1000",
        };
        var users = Names.Select(name => (name, disabled: false)).Append((Disabled, disabled: true)).ToList();
        for (var i = 0; i < users.Count; i++)
        {
            var (name, disabled) = users[i];
            settings[$"ActiveDirectory:FakeUsers:{i}:UserName"] = name;
            settings[$"ActiveDirectory:FakeUsers:{i}:Password"] = Password;
            settings[$"ActiveDirectory:FakeUsers:{i}:DisplayName"] = $"Çalışan {name}";
            settings[$"ActiveDirectory:FakeUsers:{i}:Department"] = "Muhasebe";
            settings[$"ActiveDirectory:FakeUsers:{i}:IsAllowedGroupMember"] = "false";
            settings[$"ActiveDirectory:FakeUsers:{i}:IsDisabled"] = disabled ? "true" : "false";
        }

        foreach (var (key, value) in more ?? [])
        {
            settings[key] = value;
        }

        return settings;
    }
}

internal static class AssignmentApi
{
    /// <summary>The objectGUID the employee search gives for <paramref name="userName"/>.</summary>
    public static async Task<Guid> EmployeeGuidAsync(this HttpClient client, string userName)
    {
        var found = await client.GetOkAsync($"/api/employees/search?q={Uri.EscapeDataString(userName)}");
        var person = found.GetProperty("items").EnumerateArray().Single(p => p.GetProperty("userName").GetString() == userName);
        return person.GetProperty("objectGuid").GetGuid();
    }

    public static Dictionary<string, object?> AssignBody(this JsonElement asset, Guid employee, string description = "Dizüstü bilgisayar + şarj adaptörü", string? notes = null) =>
        new()
        {
            ["employeeObjectGuid"] = employee,
            ["assignmentDescription"] = description,
            ["notes"] = notes,
            ["rowVersion"] = asset.GetProperty("rowVersion").GetString(),
        };

    public static Task<HttpResponseMessage> PostAssignmentAsync(this HttpClient client, int assetId, object body) =>
        client.SendWithCsrfAsync(HttpMethod.Post, $"/api/assets/{assetId}/assignments", body);

    public static Task<HttpResponseMessage> PostReturnAsync(this HttpClient client, int assetId, string? rowVersion) =>
        client.SendWithCsrfAsync(HttpMethod.Post, $"/api/assets/{assetId}/returns", new Dictionary<string, object?> { ["rowVersion"] = rowVersion });

    /// <summary>Assigns the asset, asserts <c>201</c> and returns the asset as saved.</summary>
    public static async Task<JsonElement> AssignAsync(this HttpClient client, JsonElement asset, Guid employee, string description = "Dizüstü bilgisayar")
    {
        using var response = await client.PostAssignmentAsync(asset.Id(), asset.AssignBody(employee, description));
        return await AssetApi.ReadAsync(response, HttpStatusCode.Created);
    }

    /// <summary>Takes the asset back, asserts <c>200</c> and returns the asset as saved.</summary>
    public static async Task<JsonElement> ReturnAsync(this HttpClient client, JsonElement asset)
    {
        using var response = await client.PostReturnAsync(asset.Id(), asset.GetProperty("rowVersion").GetString());
        return await AssetApi.ReadAsync(response, HttpStatusCode.OK);
    }

    public static string Code(this JsonElement problem) => problem.GetProperty("code").GetString()!;
}
