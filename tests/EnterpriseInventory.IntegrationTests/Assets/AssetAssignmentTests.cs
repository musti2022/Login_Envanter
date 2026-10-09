using System.Globalization;
using System.Net;
using System.Text.Json;
using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Domain.Auditing;
using EnterpriseInventory.Domain.Employees;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseInventory.IntegrationTests.Assets;

/// <summary>
/// <c>POST /api/assets/{id}/assignments</c>, <c>POST /api/assets/{id}/returns</c> and
/// <c>GET /api/assets/{id}/assignments</c> (days 22 and 23), with employees from the Development fake directory.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class AssetAssignmentTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime, IDisposable
{
    private const string User = "ayse.admin";

    private readonly FakeEmployees _people = new();
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
    public async Task An_asset_is_given_to_an_employee_who_cannot_sign_in_and_the_handover_is_recorded()
    {
        using var client = _api!.CreateSignedInClient(User);
        var asset = await client.CreateAssetAsync(_refs.NewAssetBody());
        var employee = await client.EmployeeGuidAsync(_people.First);

        using var response = await client.PostAssignmentAsync(asset.Id(), asset.AssignBody(employee, "  Dizüstü + çanta  ", " Kutusuyla "));

        var assigned = await AssetApi.ReadAsync(response, HttpStatusCode.Created);
        Assert.Equal($"/api/assets/{asset.Id()}/assignments", response.Headers.Location?.OriginalString);
        Assert.Equal("Assigned", assigned.GetProperty("status").GetString());
        Assert.NotEqual(asset.GetProperty("rowVersion").GetString(), assigned.GetProperty("rowVersion").GetString());
        var holder = assigned.GetProperty("activeAssignment");
        Assert.Equal(_people.First, holder.GetProperty("userName").GetString());
        Assert.Equal($"Çalışan {_people.First}", holder.GetProperty("displayName").GetString());
        Assert.Equal("Dizüstü + çanta", holder.GetProperty("assignmentDescription").GetString());
        Assert.Equal(User, holder.GetProperty("assignedBy").GetString());
        Assert.Equal(User, assigned.GetProperty("updatedBy").GetString());

        await using var db = fixture.CreateContext();
        var record = await db.Employees.SingleAsync(e => e.ObjectGuid == employee);
        Assert.Equal(_people.First, record.SamAccountName);
        Assert.Equal("Muhasebe", record.Department);
        Assert.True(record.IsActive);
        Assert.False(await db.AdminUsers.AnyAsync(u => u.ObjectGuid == employee));

        var audit = await AuditAsync(db, asset.Id(), AuditAction.Assigned);
        Assert.Equal(User, audit.UserName);
        using var newValues = JsonDocument.Parse(audit.NewValues!);
        Assert.Equal(_people.First, newValues.RootElement.GetProperty("employeeUserName").GetString());
        Assert.Equal("Kutusuyla", newValues.RootElement.GetProperty("notes").GetString());
        Assert.Equal(holder.GetProperty("id").GetInt32(), newValues.RootElement.GetProperty("assignmentId").GetInt32());
        Assert.Equal("Assigned", newValues.RootElement.GetProperty("status").GetString());
        Assert.Equal("""{"status":"Available"}""", audit.OldValues);

        var list = await client.GetOkAsync($"/api/assets?search={Uri.EscapeDataString(asset.GetProperty("assetCode").GetString()!)}");
        Assert.Equal(_people.First, list.GetProperty("items")[0].GetProperty("assignedUserName").GetString());
    }

    [SqlServerFact]
    public async Task A_return_closes_the_period_keeps_it_as_history_and_frees_the_asset_for_the_next_employee()
    {
        using var client = _api!.CreateSignedInClient(User);
        var asset = await client.CreateAssetAsync(_refs.NewAssetBody());
        var first = await client.AssignAsync(asset, await client.EmployeeGuidAsync(_people.Names[0]), "İlk zimmet");

        var returned = await client.ReturnAsync(first);

        Assert.Equal("Available", returned.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, returned.GetProperty("activeAssignment").ValueKind);
        var second = await client.AssignAsync(returned, await client.EmployeeGuidAsync(_people.Names[1]), "İkinci zimmet");
        Assert.Equal(_people.Names[1], second.GetProperty("activeAssignment").GetProperty("userName").GetString());

        var periods = await client.GetOkAsync($"/api/assets/{asset.Id()}/assignments");
        Assert.Equal(2, periods.GetProperty("totalCount").GetInt32());
        var (latest, earlier) = (periods.GetProperty("items")[0], periods.GetProperty("items")[1]);
        Assert.Equal("İkinci zimmet", latest.GetProperty("assignmentDescription").GetString());
        Assert.Equal(JsonValueKind.Null, latest.GetProperty("returnedAt").ValueKind);
        Assert.Equal("İlk zimmet", earlier.GetProperty("assignmentDescription").GetString());
        Assert.Equal(_people.Names[0], earlier.GetProperty("userName").GetString());
        Assert.Equal("Muhasebe", earlier.GetProperty("department").GetString());
        Assert.Equal(User, earlier.GetProperty("returnedBy").GetString());
        Assert.True(earlier.GetProperty("returnedAt").GetDateTimeOffset() >= earlier.GetProperty("assignedAt").GetDateTimeOffset());

        await using var db = fixture.CreateContext();
        var returnAudit = await AuditAsync(db, asset.Id(), AuditAction.Returned);
        using var oldValues = JsonDocument.Parse(returnAudit.OldValues!);
        Assert.Equal(_people.Names[0], oldValues.RootElement.GetProperty("employeeUserName").GetString());
        Assert.Contains("\"status\":\"Available\"", returnAudit.NewValues, StringComparison.Ordinal);

        var history = await client.GetOkAsync($"/api/assets/{asset.Id()}/history");
        Assert.Equal(
            ["Assigned", "Returned", "Assigned", "Created"],
            history.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("action").GetString()));
    }

    [SqlServerFact]
    public async Task The_assignment_history_of_an_archived_asset_stays_readable()
    {
        using var client = _api!.CreateSignedInClient(User);
        var asset = await client.CreateAssetAsync(_refs.NewAssetBody());
        var returned = await client.ReturnAsync(await client.AssignAsync(asset, await client.EmployeeGuidAsync(_people.First)));
        using (var archive = await client.SendWithCsrfAsync(
            HttpMethod.Delete, $"/api/assets/{asset.Id()}?rowVersion={Uri.EscapeDataString(returned.GetProperty("rowVersion").GetString()!)}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        }

        var periods = await client.GetOkAsync($"/api/assets/{asset.Id()}/assignments");

        Assert.Equal(_people.First, Assert.Single(periods.GetProperty("items").EnumerateArray()).GetProperty("userName").GetString());
    }

    [SqlServerFact]
    public async Task The_employee_record_is_refreshed_from_the_directory_on_each_assignment()
    {
        using var client = _api!.CreateSignedInClient(User);
        var employee = await client.EmployeeGuidAsync(_people.First);
        await fixture.SaveAsync(Employee.Create(employee, "eski.ad", "Eski Görünen Ad", null, "Eski Birim", null, isActive: true, fixture.Clock.GetUtcNow()));
        var asset = await client.CreateAssetAsync(_refs.NewAssetBody());

        await client.AssignAsync(asset, employee);

        await using var db = fixture.CreateContext();
        var record = await db.Employees.SingleAsync(e => e.ObjectGuid == employee);
        Assert.Equal(_people.First, record.SamAccountName);
        Assert.Equal($"Çalışan {_people.First}", record.DisplayName);
        Assert.Equal("Muhasebe", record.Department);
        Assert.True(record.LastSyncedAt > fixture.Clock.GetUtcNow());
    }

    public static TheoryData<string, object?, string> InvalidBodies => new()
    {
        { "employeeObjectGuid", null, "Zimmetlenecek çalışanı seçin." },
        { "employeeObjectGuid", Guid.Empty, "Zimmetlenecek çalışanı seçin." },
        { "assignmentDescription", null, "Zimmet tanımı zorunludur." },
        { "assignmentDescription", "   ", "Zimmet tanımı zorunludur." },
        { "assignmentDescription", new string('x', AssetAssignment.AssignmentDescriptionMaxLength + 1), "Zimmet tanımı en fazla 500 karakter olabilir." },
        { "notes", new string('x', AssetAssignment.NotesMaxLength + 1), "Not en fazla 1000 karakter olabilir." },
        { "rowVersion", null, "Kayıt sürümü (rowVersion) zorunludur." },
        { "rowVersion", "bozuk", "Kayıt sürümü (rowVersion) geçersiz; kaydı yeniden açıp tekrar deneyin." },
    };

    [SqlServerTheory]
    [MemberData(nameof(InvalidBodies))]
    public async Task An_invalid_assignment_is_refused_with_a_turkish_field_error_and_changes_nothing(string field, object? value, string message)
    {
        using var client = _api!.CreateSignedInClient(User);
        var asset = await client.CreateAssetAsync(_refs.NewAssetBody());
        var body = asset.AssignBody(await client.EmployeeGuidAsync(_people.First));
        body[field] = value;

        using var response = await client.PostAssignmentAsync(asset.Id(), body);

        Assert.Equal(message, (await AssetApi.ReadAsync(response, HttpStatusCode.BadRequest)).FieldError(field));
        await AssertStillAvailableAsync(client, asset);
    }

    [SqlServerFact]
    public async Task Someone_the_directory_does_not_know_cannot_be_given_an_asset()
    {
        using var client = _api!.CreateSignedInClient(User);
        var asset = await client.CreateAssetAsync(_refs.NewAssetBody());

        using var response = await client.PostAssignmentAsync(asset.Id(), asset.AssignBody(Guid.NewGuid()));

        var problem = await AssetApi.ReadAsync(response, HttpStatusCode.BadRequest);
        Assert.StartsWith("Seçilen çalışan Active Directory'de bulunamadı", problem.FieldError("employeeObjectGuid"), StringComparison.Ordinal);
        await AssertStillAvailableAsync(client, asset);
    }

    [SqlServerFact]
    public async Task An_employee_whose_account_is_disabled_cannot_be_given_an_asset()
    {
        using var client = _api!.CreateSignedInClient(User);
        var asset = await client.CreateAssetAsync(_refs.NewAssetBody());

        // Disabled accounts are not offered by the search; the identity the directory would give is used instead.
        using var response = await client.PostAssignmentAsync(asset.Id(), asset.AssignBody(FakeGuid(_people.Disabled)));

        Assert.Equal("employee_inactive", (await AssetApi.ReadAsync(response, HttpStatusCode.Conflict)).Code());
        await AssertStillAvailableAsync(client, asset);
    }

    [SqlServerFact]
    public async Task An_assignment_based_on_an_old_version_of_the_asset_is_refused_with_409()
    {
        using var client = _api!.CreateSignedInClient(User);
        var asset = await client.CreateAssetAsync(_refs.NewAssetBody());
        var edit = asset.ToUpdateBody();
        edit["description"] = "Başka biri değiştirdi";
        using (var updated = await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{asset.Id()}", edit))
        {
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        }

        using var response = await client.PostAssignmentAsync(asset.Id(), asset.AssignBody(await client.EmployeeGuidAsync(_people.First)));

        Assert.Equal("concurrency_conflict", (await AssetApi.ReadAsync(response, HttpStatusCode.Conflict)).Code());
        Assert.Equal("Available", (await client.GetOkAsync($"/api/assets/{asset.Id()}")).GetProperty("status").GetString());
    }

    [SqlServerFact]
    public async Task An_assigned_asset_cannot_be_assigned_again_until_it_is_returned()
    {
        using var client = _api!.CreateSignedInClient(User);
        var assigned = await client.AssignAsync(await client.CreateAssetAsync(_refs.NewAssetBody()), await client.EmployeeGuidAsync(_people.Names[0]));

        using var response = await client.PostAssignmentAsync(assigned.Id(), assigned.AssignBody(await client.EmployeeGuidAsync(_people.Names[1])));

        Assert.Equal("asset_already_assigned", (await AssetApi.ReadAsync(response, HttpStatusCode.Conflict)).Code());
        var current = await client.GetOkAsync($"/api/assets/{assigned.Id()}");
        Assert.Equal(_people.Names[0], current.GetProperty("activeAssignment").GetProperty("userName").GetString());
    }

    [SqlServerTheory]
    [InlineData("Faulty")]
    [InlineData("Retired")]
    public async Task Only_an_available_asset_can_be_assigned(string status)
    {
        using var client = _api!.CreateSignedInClient(User);
        var body = _refs.NewAssetBody();
        body["status"] = status;
        var asset = await client.CreateAssetAsync(body);

        using var response = await client.PostAssignmentAsync(asset.Id(), asset.AssignBody(await client.EmployeeGuidAsync(_people.First)));

        Assert.Equal("asset_not_available", (await AssetApi.ReadAsync(response, HttpStatusCode.Conflict)).Code());
    }

    [SqlServerFact]
    public async Task Archived_and_missing_assets_cannot_be_assigned_or_returned()
    {
        using var client = _api!.CreateSignedInClient(User);
        var asset = await client.CreateAssetAsync(_refs.NewAssetBody());
        var rowVersion = asset.GetProperty("rowVersion").GetString()!;
        using (var archive = await client.SendWithCsrfAsync(HttpMethod.Delete, $"/api/assets/{asset.Id()}?rowVersion={Uri.EscapeDataString(rowVersion)}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        }

        var archived = await client.GetOkAsync($"/api/assets/{asset.Id()}");
        var employee = await client.EmployeeGuidAsync(_people.First);
        using var assign = await client.PostAssignmentAsync(asset.Id(), archived.AssignBody(employee));
        using var giveBack = await client.PostReturnAsync(asset.Id(), archived.GetProperty("rowVersion").GetString());
        using var missing = await client.PostAssignmentAsync(int.MaxValue, archived.AssignBody(employee));
        using var missingReturn = await client.PostReturnAsync(int.MaxValue, rowVersion);
        using var missingList = await client.GetAsync(AssetApi.Uri($"/api/assets/{int.MaxValue}/assignments"));

        Assert.Equal("asset_archived", (await AssetApi.ReadAsync(assign, HttpStatusCode.Conflict)).Code());
        Assert.Equal("asset_archived", (await AssetApi.ReadAsync(giveBack, HttpStatusCode.Conflict)).Code());
        Assert.Equal("asset_not_found", (await AssetApi.ReadAsync(missing, HttpStatusCode.NotFound)).Code());
        Assert.Equal("asset_not_found", (await AssetApi.ReadAsync(missingReturn, HttpStatusCode.NotFound)).Code());
        Assert.Equal("asset_not_found", (await AssetApi.ReadAsync(missingList, HttpStatusCode.NotFound)).Code());
    }

    [SqlServerFact]
    public async Task An_asset_nobody_holds_cannot_be_returned()
    {
        using var client = _api!.CreateSignedInClient(User);
        var asset = await client.CreateAssetAsync(_refs.NewAssetBody());

        using var response = await client.PostReturnAsync(asset.Id(), asset.GetProperty("rowVersion").GetString());

        Assert.Equal("asset_not_assigned", (await AssetApi.ReadAsync(response, HttpStatusCode.Conflict)).Code());
    }

    [SqlServerFact]
    public async Task A_return_based_on_an_old_version_is_refused_and_the_asset_stays_assigned()
    {
        using var client = _api!.CreateSignedInClient(User);
        var asset = await client.CreateAssetAsync(_refs.NewAssetBody());
        await client.AssignAsync(asset, await client.EmployeeGuidAsync(_people.First));

        using var response = await client.PostReturnAsync(asset.Id(), asset.GetProperty("rowVersion").GetString());
        using var missing = await client.PostReturnAsync(asset.Id(), null);

        Assert.Equal("concurrency_conflict", (await AssetApi.ReadAsync(response, HttpStatusCode.Conflict)).Code());
        Assert.Equal("Kayıt sürümü (rowVersion) zorunludur.", (await AssetApi.ReadAsync(missing, HttpStatusCode.BadRequest)).FieldError("rowVersion"));
        Assert.Equal("Assigned", (await client.GetOkAsync($"/api/assets/{asset.Id()}")).GetProperty("status").GetString());
    }

    [SqlServerFact]
    public async Task An_unreachable_directory_stops_the_assignment_before_anything_is_written()
    {
        using var client = _api!.CreateSignedInClient(User);
        var asset = await client.CreateAssetAsync(_refs.NewAssetBody());
        var employee = await client.EmployeeGuidAsync(_people.First);
        await using var ldapApi = new TestApiFactory(fixture.ConnectionString, settings: new Dictionary<string, string?> { ["RateLimiting:PermitLimit"] = "1000" });
        using var ldapClient = ldapApi.CreateSignedInClient(User);

        using var response = await ldapClient.PostAssignmentAsync(asset.Id(), asset.AssignBody(employee));

        Assert.Equal("directory_unavailable", (await AssetApi.ReadAsync(response, HttpStatusCode.ServiceUnavailable)).Code());
        await AssertStillAvailableAsync(client, asset);
    }

    [SqlServerTheory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=101")]
    public async Task Paging_the_assignments_is_checked(string query)
    {
        using var client = _api!.CreateSignedInClient(User);
        var asset = await client.CreateAssetAsync(_refs.NewAssetBody());

        using var response = await client.GetAsync(AssetApi.Uri($"/api/assets/{asset.Id()}/assignments{query}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>The objectGUID the fake directory derives from a user name (see FakeDirectoryService).</summary>
    private static Guid FakeGuid(string userName) =>
        new(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("fake-directory:" + userName.ToUpperInvariant())).AsSpan(0, 16));

    private static async Task<AuditLog> AuditAsync(Infrastructure.Persistence.ApplicationDbContext db, int assetId, AuditAction action)
    {
        var entityId = assetId.ToString(CultureInfo.InvariantCulture);
        return await db.AuditLogs.Where(l => l.EntityName == nameof(Asset) && l.EntityId == entityId && l.Action == action).OrderBy(l => l.Id).FirstAsync();
    }

    private async Task AssertStillAvailableAsync(HttpClient client, JsonElement asset)
    {
        var current = await client.GetOkAsync($"/api/assets/{asset.Id()}");
        Assert.Equal("Available", current.GetProperty("status").GetString());
        Assert.Equal(asset.GetProperty("rowVersion").GetString(), current.GetProperty("rowVersion").GetString());
        await using var db = fixture.CreateContext();
        Assert.False(await db.AssetAssignments.AnyAsync(x => x.AssetId == asset.Id()));
        var entityId = asset.Id().ToString(CultureInfo.InvariantCulture);
        Assert.Equal([AuditAction.Created], await db.AuditLogs.Where(l => l.EntityName == nameof(Asset) && l.EntityId == entityId).Select(l => l.Action).ToListAsync());
    }
}
