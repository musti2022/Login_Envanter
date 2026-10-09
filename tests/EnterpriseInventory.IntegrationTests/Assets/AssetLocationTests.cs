using System.Globalization;
using System.Net;
using System.Text.Json;
using EnterpriseInventory.Application.Assets;
using EnterpriseInventory.Domain.Auditing;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseInventory.IntegrationTests.Assets;

/// <summary>
/// Day 25, <c>PUT /api/assets/{id}/location</c>: a move is checked against the lookups (they exist, newly chosen
/// ones are active, the location is in the city), against the version the caller read, and is recorded as one
/// <c>LocationChanged</c> audit record; nothing else of the asset changes.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class AssetLocationTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime, IDisposable
{
    private const string User = "ayse.admin";

    private readonly FakeEmployees _people = new(1);
    private TestApiFactory? _api;
    private InventoryReferences _refs = null!;

    public async Task InitializeAsync()
    {
        if (SqlServerDatabaseFixture.ServerConnectionString is null)
        {
            return;
        }

        _refs = await InventoryReferences.SeedAsync(fixture);
        _api = new TestApiFactory(fixture.ConnectionString, settings: _people.Settings());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _api?.Dispose();

    [SqlServerFact]
    public async Task A_move_changes_only_where_the_asset_is_and_is_recorded_as_a_location_change()
    {
        using var client = _api!.CreateSignedInClient(User);
        var asset = await client.CreateAssetAsync(_refs.NewAssetBody());

        using var response = await client.PutLocationAsync(asset.Id(), Move(asset, _refs.SecondCityId, _refs.SecondDepartmentId, _refs.SecondCityLocationId));
        var moved = await AssetApi.ReadAsync(response, HttpStatusCode.OK);

        Assert.Equal(_refs.SecondCityId, moved.GetProperty("city").GetProperty("id").GetInt32());
        Assert.Equal(_refs.SecondDepartmentId, moved.GetProperty("department").GetProperty("id").GetInt32());
        Assert.Equal(_refs.SecondCityLocationId, moved.GetProperty("location").GetProperty("id").GetInt32());
        Assert.NotEqual(asset.GetProperty("rowVersion").GetString(), moved.GetProperty("rowVersion").GetString());
        Assert.Equal(User, moved.GetProperty("updatedBy").GetString());
        foreach (var unchanged in new[] { "assetCode", "computerName", "serialNumber", "description", "status", "assetType" })
        {
            Assert.Equal(asset.GetProperty(unchanged).ToString(), moved.GetProperty(unchanged).ToString());
        }

        var entry = (await client.GetOkAsync($"/api/assets/{asset.Id()}/history")).GetProperty("items")[0];
        Assert.Equal(nameof(AuditAction.LocationChanged), entry.GetProperty("action").GetString());
        var oldValues = entry.GetProperty("oldValues");
        var newValues = entry.GetProperty("newValues");
        Assert.Equal(_refs.CityId, oldValues.GetProperty("cityId").GetInt32());
        Assert.Equal(_refs.SecondCityId, newValues.GetProperty("cityId").GetInt32());
        Assert.Equal(_refs.LocationId, oldValues.GetProperty("locationId").GetInt32());
        Assert.Equal(_refs.SecondCityLocationId, newValues.GetProperty("locationId").GetInt32());
        Assert.Equal(moved.GetProperty("department").GetProperty("name").GetString(), newValues.GetProperty("departmentName").GetString());
        Assert.Equal(
            ["cityId", "cityName", "departmentId", "departmentName", "locationId", "locationName"],
            newValues.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));

        var list = await client.GetOkAsync($"/api/assets?cityId={_refs.SecondCityId}&locationId={_refs.SecondCityLocationId}");
        Assert.Contains(asset.GetProperty("assetCode").GetString(), list.Codes());
    }

    [SqlServerFact]
    public async Task An_assigned_asset_moves_with_its_holder()
    {
        using var client = _api!.CreateSignedInClient(User);
        var assigned = await client.AssignAsync(await client.CreateAssetAsync(_refs.NewAssetBody()), await client.EmployeeGuidAsync(_people.First));

        using var response = await client.PutLocationAsync(assigned.Id(), Move(assigned, _refs.CityId, _refs.SecondDepartmentId, locationId: null));
        var moved = await AssetApi.ReadAsync(response, HttpStatusCode.OK);

        Assert.Equal("Assigned", moved.GetProperty("status").GetString());
        Assert.Equal(_people.First, moved.GetProperty("activeAssignment").GetProperty("userName").GetString());
        Assert.Equal(JsonValueKind.Null, moved.GetProperty("location").ValueKind);
        Assert.Equal(_refs.SecondDepartmentId, moved.GetProperty("department").GetProperty("id").GetInt32());
    }

    [SqlServerFact]
    public async Task Each_wrong_value_is_refused_under_its_field_in_Turkish_and_nothing_is_written()
    {
        using var client = _api!.CreateSignedInClient(User);
        var asset = await client.CreateAssetAsync(_refs.NewAssetBody());
        var cases = new (Dictionary<string, object?> Body, string Field, string Message)[]
        {
            (Move(asset, null, _refs.DepartmentId, null), "cityId", "Şehir seçilmelidir."),
            (Move(asset, _refs.CityId, null, null), "departmentId", "Departman seçilmelidir."),
            (Move(asset, 0, _refs.DepartmentId, null), "cityId", "Şehir geçersiz."),
            (Move(asset, _refs.CityId, _refs.DepartmentId, -1), "locationId", "Lokasyon geçersiz."),
            (Move(asset, int.MaxValue, _refs.DepartmentId, null), "cityId", AssetMessages.CityNotFound),
            (Move(asset, _refs.CityId, int.MaxValue, null), "departmentId", AssetMessages.DepartmentNotFound),
            (Move(asset, _refs.CityId, _refs.DepartmentId, int.MaxValue), "locationId", AssetMessages.LocationNotFound),
            (Move(asset, _refs.CityId, _refs.DepartmentId, _refs.SecondCityLocationId), "locationId", AssetMessages.LocationInAnotherCity),
            (Move(asset, _refs.InactiveCityId, _refs.DepartmentId, null), "cityId", AssetMessages.CityInactive),
            (Move(asset, _refs.CityId, _refs.InactiveDepartmentId, null), "departmentId", AssetMessages.DepartmentInactive),
            (Move(asset, _refs.CityId, _refs.DepartmentId, _refs.InactiveLocationId), "locationId", AssetMessages.LocationInactive),
            (new Dictionary<string, object?>(Move(asset, _refs.CityId, _refs.DepartmentId, null)) { ["rowVersion"] = null }, "rowVersion", "Kayıt sürümü (rowVersion) zorunludur."),
        };

        foreach (var (body, field, message) in cases)
        {
            using var response = await client.PutLocationAsync(asset.Id(), body);
            var problem = await AssetApi.ReadAsync(response, HttpStatusCode.BadRequest);
            Assert.Equal(message, problem.FieldError(field));
        }

        var current = await client.GetOkAsync($"/api/assets/{asset.Id()}");
        Assert.Equal(asset.GetProperty("rowVersion").GetString(), current.GetProperty("rowVersion").GetString());
        Assert.Equal([nameof(AuditAction.Created)], await ActionsAsync(client, asset.Id()));
    }

    [SqlServerFact]
    public async Task A_value_the_asset_already_has_may_have_been_deactivated_meanwhile()
    {
        using var client = _api!.CreateSignedInClient(User);
        var refs = await InventoryReferences.SeedAsync(fixture);
        var asset = await client.CreateAssetAsync(refs.NewAssetBody());
        await using (var db = fixture.CreateContext())
        {
            (await db.Departments.SingleAsync(d => d.Id == refs.DepartmentId)).Deactivate();
            await db.SaveChangesAsync();
        }

        using var response = await client.PutLocationAsync(asset.Id(), Move(asset, refs.CityId, refs.DepartmentId, refs.SecondLocationId));
        var moved = await AssetApi.ReadAsync(response, HttpStatusCode.OK);

        Assert.Equal(refs.DepartmentId, moved.GetProperty("department").GetProperty("id").GetInt32());
        Assert.Equal(refs.SecondLocationId, moved.GetProperty("location").GetProperty("id").GetInt32());
    }

    [SqlServerFact]
    public async Task A_move_of_an_older_version_gets_409_and_changes_nothing()
    {
        using var client = _api!.CreateSignedInClient(User);
        var asset = await client.CreateAssetAsync(_refs.NewAssetBody());
        using (var first = await client.PutLocationAsync(asset.Id(), Move(asset, _refs.CityId, _refs.SecondDepartmentId, _refs.LocationId)))
        {
            await AssetApi.ReadAsync(first, HttpStatusCode.OK);
        }

        // A second user read the asset before the first move and now moves it elsewhere.
        using var response = await client.PutLocationAsync(asset.Id(), Move(asset, _refs.SecondCityId, _refs.DepartmentId, null));

        var problem = await AssetApi.ReadAsync(response, HttpStatusCode.Conflict);
        Assert.Equal("concurrency_conflict", problem.Code());
        Assert.Equal("Kayıt siz düzenlerken başka bir kullanıcı tarafından değiştirildi.", problem.GetProperty("title").GetString());
        var current = await client.GetOkAsync($"/api/assets/{asset.Id()}");
        Assert.Equal(_refs.CityId, current.GetProperty("city").GetProperty("id").GetInt32());
        Assert.Equal(_refs.SecondDepartmentId, current.GetProperty("department").GetProperty("id").GetInt32());
        Assert.Equal([nameof(AuditAction.LocationChanged), nameof(AuditAction.Created)], await ActionsAsync(client, asset.Id()));
    }

    [SqlServerFact]
    public async Task Moving_to_where_the_asset_already_is_writes_nothing()
    {
        using var client = _api!.CreateSignedInClient(User);
        var asset = await client.CreateAssetAsync(_refs.NewAssetBody());

        using var response = await client.PutLocationAsync(asset.Id(), Move(asset, _refs.CityId, _refs.DepartmentId, _refs.LocationId));
        var same = await AssetApi.ReadAsync(response, HttpStatusCode.OK);

        Assert.Equal(asset.GetProperty("rowVersion").GetString(), same.GetProperty("rowVersion").GetString());
        Assert.Equal([nameof(AuditAction.Created)], await ActionsAsync(client, asset.Id()));
    }

    [SqlServerFact]
    public async Task An_archived_asset_does_not_move_and_a_missing_one_is_not_found()
    {
        using var client = _api!.CreateSignedInClient(User);
        var asset = await client.CreateAssetAsync(_refs.NewAssetBody());
        using (var archive = await client.SendWithCsrfAsync(
            HttpMethod.Delete, $"/api/assets/{asset.Id()}?rowVersion={Uri.EscapeDataString(asset.GetProperty("rowVersion").GetString()!)}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        }

        var archived = await client.GetOkAsync($"/api/assets/{asset.Id()}");
        using var response = await client.PutLocationAsync(asset.Id(), Move(archived, _refs.SecondCityId, _refs.DepartmentId, null));
        Assert.Equal("asset_archived", (await AssetApi.ReadAsync(response, HttpStatusCode.Conflict)).Code());

        using var missing = await client.PutLocationAsync(int.MaxValue, Move(asset, _refs.CityId, _refs.DepartmentId, null));
        Assert.Equal("asset_not_found", (await AssetApi.ReadAsync(missing, HttpStatusCode.NotFound)).Code());
    }

    /// <summary>A move of the asset as read: its row version goes with it.</summary>
    private static Dictionary<string, object?> Move(JsonElement asset, int? cityId, int? departmentId, int? locationId) => new()
    {
        ["cityId"] = cityId,
        ["departmentId"] = departmentId,
        ["locationId"] = locationId,
        ["rowVersion"] = asset.GetProperty("rowVersion").GetString(),
    };

    private static async Task<List<string>> ActionsAsync(HttpClient client, int assetId) =>
        (await client.GetOkAsync($"/api/assets/{assetId}/history")).GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("action").GetString()!)
            .ToList();
}

internal static class AssetLocationApi
{
    public static Task<HttpResponseMessage> PutLocationAsync(this HttpClient client, int assetId, Dictionary<string, object?> body) =>
        client.SendWithCsrfAsync(HttpMethod.Put, string.Create(CultureInfo.InvariantCulture, $"/api/assets/{assetId}/location"), body);
}
