using System.Net;
using System.Text.Json;
using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseInventory.IntegrationTests.Assets;

/// <summary>
/// <c>DELETE /api/assets/{id}</c> archives (soft-deletes) an asset and <c>GET /api/assets/{id}/history</c> shows its
/// audit trail: an archived asset leaves the list but keeps its row, assignments and history, and stays read-only.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class AssetArchiveTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime, IDisposable
{
    private const string User = "ayse.admin";

    private TestApiFactory? _api;
    private InventoryReferences _refs = null!;

    public async Task InitializeAsync()
    {
        if (SqlServerDatabaseFixture.ServerConnectionString is null)
        {
            return;
        }

        _refs = await InventoryReferences.SeedAsync(fixture);
        _api = new TestApiFactory(fixture.ConnectionString, settings: new Dictionary<string, string?> { ["RateLimiting:PermitLimit"] = "1000" });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _api?.Dispose();

    [SqlServerFact]
    public async Task An_archived_asset_leaves_the_list_but_can_still_be_read()
    {
        using var client = _api!.CreateSignedInClient(User);
        var created = await client.CreateAssetAsync(_refs.NewAssetBody());
        var listedBefore = (await client.GetOkAsync("/api/assets?pageSize=1")).GetProperty("totalCount").GetInt32();

        using var response = await client.SendWithCsrfAsync(HttpMethod.Delete, ArchivePath(created));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var archived = await client.GetOkAsync($"/api/assets/{created.Id()}");
        Assert.True(archived.GetProperty("isArchived").GetBoolean());
        Assert.Equal(created.GetProperty("assetCode").GetString(), archived.GetProperty("assetCode").GetString());
        Assert.Equal(User, archived.GetProperty("updatedBy").GetString());
        Assert.Equal(listedBefore - 1, (await client.GetOkAsync("/api/assets?pageSize=1")).GetProperty("totalCount").GetInt32());
    }

    [SqlServerFact]
    public async Task The_history_keeps_every_change_up_to_the_archiving_newest_first()
    {
        using var client = _api!.CreateSignedInClient(User);
        var created = await client.CreateAssetAsync(_refs.NewAssetBody());
        var edit = created.ToUpdateBody();
        edit["description"] = "Arşivden önce";
        JsonElement edited;
        using (var response = await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{created.Id()}", edit))
        {
            edited = await AssetApi.ReadAsync(response, HttpStatusCode.OK);
        }

        using var archive = await client.SendWithCsrfAsync(HttpMethod.Delete, ArchivePath(edited));
        Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);

        var history = await client.GetOkAsync($"/api/assets/{created.Id()}/history");
        var entries = history.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["Archived", "Updated", "Created"], entries.Select(e => e.GetProperty("action").GetString()));
        Assert.Equal(3, history.GetProperty("totalCount").GetInt32());

        var archived = entries[0];
        Assert.Equal(User, archived.GetProperty("userName").GetString());
        Assert.Equal(archive.Headers.GetValues("X-Correlation-ID").Single(), archived.GetProperty("correlationId").GetString());
        Assert.False(archived.GetProperty("oldValues").GetProperty("isArchived").GetBoolean());
        Assert.True(archived.GetProperty("newValues").GetProperty("isArchived").GetBoolean());
        Assert.Equal("Arşivden önce", entries[1].GetProperty("newValues").GetProperty("description").GetString());
        Assert.Equal(JsonValueKind.Null, entries[2].GetProperty("oldValues").ValueKind);
        Assert.Equal(created.GetProperty("assetCode").GetString(), entries[2].GetProperty("newValues").GetProperty("assetCode").GetString());
    }

    [SqlServerFact]
    public async Task Archiving_keeps_the_assignment_history()
    {
        using var client = _api!.CreateSignedInClient(User);
        var assignedAt = fixture.Clock.GetUtcNow().AddDays(-10);
        var id = await _refs.SaveAssetAsync(fixture, asset =>
        {
            asset.Assign(PersistenceTestData.NewEmployee(assignedAt), "Eski zimmet", notes: null, "mehmet.admin", assignedAt);
            asset.Return("mehmet.admin", assignedAt.AddDays(3));
        });

        using var response = await client.SendWithCsrfAsync(HttpMethod.Delete, ArchivePath(await client.GetOkAsync($"/api/assets/{id}")));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var db = fixture.CreateContext();
        var asset = await db.Assets.IgnoreQueryFilters().Include(a => a.Assignments).SingleAsync(a => a.Id == id);
        Assert.True(asset.IsDeleted);
        var assignment = Assert.Single(asset.Assignments);
        Assert.Equal("Eski zimmet", assignment.AssignmentDescription);
        Assert.Equal(assignedAt.AddDays(3), assignment.ReturnedAt);
    }

    [SqlServerFact]
    public async Task An_archived_asset_cannot_be_edited_or_archived_again_and_keeps_its_code()
    {
        using var client = _api!.CreateSignedInClient(User);
        var body = _refs.NewAssetBody();
        var created = await client.CreateAssetAsync(body);
        using (var response = await client.SendWithCsrfAsync(HttpMethod.Delete, ArchivePath(created)))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var archived = await client.GetOkAsync($"/api/assets/{created.Id()}");
        var edit = archived.ToUpdateBody();
        edit["description"] = "Arşivden sonra";
        using var update = await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{created.Id()}", edit);
        using var again = await client.SendWithCsrfAsync(HttpMethod.Delete, ArchivePath(archived));
        using var reuse = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/assets", _refs.NewAssetBody((string)body["assetCode"]!));

        Assert.Equal("asset_archived", (await AssetApi.ReadAsync(update, HttpStatusCode.Conflict)).GetProperty("code").GetString());
        var alreadyArchived = await AssetApi.ReadAsync(again, HttpStatusCode.Conflict);
        Assert.Equal("asset_already_archived", alreadyArchived.GetProperty("code").GetString());
        Assert.Equal("Demirbaş zaten arşivlenmiş.", alreadyArchived.GetProperty("title").GetString());
        Assert.Equal("duplicate_value", (await AssetApi.ReadAsync(reuse, HttpStatusCode.Conflict)).GetProperty("code").GetString());
        Assert.Equal(2, (await client.GetOkAsync($"/api/assets/{created.Id()}/history")).GetProperty("totalCount").GetInt32());
    }

    [SqlServerFact]
    public async Task An_assigned_asset_cannot_be_archived()
    {
        using var client = _api!.CreateSignedInClient(User);
        var id = await _refs.SaveAssetAsync(fixture, asset => asset.Assign(
            PersistenceTestData.NewEmployee(fixture.Clock.GetUtcNow()), "Zimmet", notes: null, "mehmet.admin", fixture.Clock.GetUtcNow()));
        var assigned = await client.GetOkAsync($"/api/assets/{id}");

        using var response = await client.SendWithCsrfAsync(HttpMethod.Delete, ArchivePath(assigned));
        var problem = await AssetApi.ReadAsync(response, HttpStatusCode.Conflict);

        Assert.Equal("asset_assigned", problem.GetProperty("code").GetString());
        Assert.Equal("Önce demirbaşın iadesini alın, sonra tekrar deneyin.", problem.GetProperty("detail").GetString());
        Assert.False((await client.GetOkAsync($"/api/assets/{id}")).GetProperty("isArchived").GetBoolean());
    }

    [SqlServerFact]
    public async Task Archiving_an_older_version_gets_409_and_archives_nothing()
    {
        using var client = _api!.CreateSignedInClient(User);
        var created = await client.CreateAssetAsync(_refs.NewAssetBody());
        var edit = created.ToUpdateBody();
        edit["description"] = "Başkası düzenledi";
        using (var response = await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{created.Id()}", edit))
        {
            await AssetApi.ReadAsync(response, HttpStatusCode.OK);
        }

        using var stale = await client.SendWithCsrfAsync(HttpMethod.Delete, ArchivePath(created));

        Assert.Equal("concurrency_conflict", (await AssetApi.ReadAsync(stale, HttpStatusCode.Conflict)).GetProperty("code").GetString());
        Assert.False((await client.GetOkAsync($"/api/assets/{created.Id()}")).GetProperty("isArchived").GetBoolean());
    }

    [SqlServerFact]
    public async Task An_edit_and_an_archive_of_the_same_version_at_once_save_exactly_one()
    {
        using var editor = _api!.CreateSignedInClient(User);
        using var archiver = _api.CreateSignedInClient("mehmet.admin");
        var created = await editor.CreateAssetAsync(_refs.NewAssetBody());
        var edit = created.ToUpdateBody();
        edit["description"] = "Aynı anda";

        var responses = await Task.WhenAll(
            editor.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{created.Id()}", edit),
            archiver.SendWithCsrfAsync(HttpMethod.Delete, ArchivePath(created)));
        try
        {
            Assert.Single(responses, r => r.IsSuccessStatusCode);
            var loser = Assert.Single(responses, r => !r.IsSuccessStatusCode);
            Assert.Equal("concurrency_conflict", (await AssetApi.ReadAsync(loser, HttpStatusCode.Conflict)).GetProperty("code").GetString());
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        var history = await editor.GetOkAsync($"/api/assets/{created.Id()}/history");
        Assert.Equal(2, history.GetProperty("totalCount").GetInt32());
    }

    [SqlServerTheory]
    [InlineData("", "Kayıt sürümü (rowVersion) zorunludur.")]
    [InlineData("?rowVersion=", "Kayıt sürümü (rowVersion) zorunludur.")]
    [InlineData("?rowVersion=AAAAAAAAB9A", "Kayıt sürümü (rowVersion) geçersiz; kaydı yeniden açıp tekrar deneyin.")]
    public async Task Archiving_needs_the_row_version(string query, string message)
    {
        using var client = _api!.CreateSignedInClient(User);
        var created = await client.CreateAssetAsync(_refs.NewAssetBody());

        using var response = await client.SendWithCsrfAsync(HttpMethod.Delete, $"/api/assets/{created.Id()}{query}");

        Assert.Equal(message, (await AssetApi.ReadAsync(response, HttpStatusCode.BadRequest)).FieldError("rowVersion"));
        Assert.False((await client.GetOkAsync($"/api/assets/{created.Id()}")).GetProperty("isArchived").GetBoolean());
    }

    [SqlServerFact]
    public async Task Archiving_without_the_csrf_token_is_refused()
    {
        using var client = _api!.CreateSignedInClient(User);
        var created = await client.CreateAssetAsync(_refs.NewAssetBody());

        using var response = await client.DeleteAsync(AssetApi.Uri(ArchivePath(created)));

        Assert.Equal("csrf_invalid", (await AssetApi.ReadAsync(response, HttpStatusCode.BadRequest)).GetProperty("code").GetString());
        Assert.False((await client.GetOkAsync($"/api/assets/{created.Id()}")).GetProperty("isArchived").GetBoolean());
    }

    [SqlServerFact]
    public async Task The_history_is_paged()
    {
        using var client = _api!.CreateSignedInClient(User);
        var asset = await client.CreateAssetAsync(_refs.NewAssetBody());
        foreach (var description in new[] { "Bir", "İki", "Üç" })
        {
            var edit = asset.ToUpdateBody();
            edit["description"] = description;
            using var response = await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{asset.Id()}", edit);
            asset = await AssetApi.ReadAsync(response, HttpStatusCode.OK);
        }

        var first = await client.GetOkAsync($"/api/assets/{asset.Id()}/history?pageSize=2");
        var second = await client.GetOkAsync($"/api/assets/{asset.Id()}/history?page=2&pageSize=2");

        static IEnumerable<string?> Descriptions(JsonElement page) => page.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("newValues").TryGetProperty("description", out var d) ? d.GetString() : null);
        Assert.Equal(["Üç", "İki"], Descriptions(first));
        Assert.Equal(["Bir", "Test demirbaşı"], Descriptions(second));
        Assert.Equal(4, first.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, first.GetProperty("totalPages").GetInt32());
    }

    [SqlServerTheory]
    [InlineData("?pageSize=0", "pageSize", "Sayfa boyutu 1 ile 100 arasında olmalıdır.")]
    [InlineData("?page=0", "page", "Sayfa numarası 1 ile 100000 arasında olmalıdır.")]
    public async Task Invalid_history_paging_is_refused(string query, string field, string message)
    {
        using var client = _api!.CreateSignedInClient(User);
        var created = await client.CreateAssetAsync(_refs.NewAssetBody());

        using var response = await client.GetAsync(AssetApi.Uri($"/api/assets/{created.Id()}/history{query}"));

        Assert.Equal(message, (await AssetApi.ReadAsync(response, HttpStatusCode.BadRequest)).FieldError(field));
    }

    [SqlServerFact]
    public async Task An_unknown_asset_has_no_history_and_cannot_be_archived()
    {
        using var client = _api!.CreateSignedInClient(User);
        var rowVersion = Uri.EscapeDataString(Convert.ToBase64String(new byte[8]));

        using var history = await client.GetAsync(AssetApi.Uri("/api/assets/987654321/history"));
        using var archive = await client.SendWithCsrfAsync(HttpMethod.Delete, $"/api/assets/987654321?rowVersion={rowVersion}");

        Assert.Equal("asset_not_found", (await AssetApi.ReadAsync(history, HttpStatusCode.NotFound)).GetProperty("code").GetString());
        Assert.Equal("asset_not_found", (await AssetApi.ReadAsync(archive, HttpStatusCode.NotFound)).GetProperty("code").GetString());
    }

    /// <summary>The archive URL for the version of the asset the caller read; base64 has to be URL-encoded.</summary>
    private static string ArchivePath(JsonElement asset) =>
        $"/api/assets/{asset.Id()}?rowVersion={Uri.EscapeDataString(asset.GetProperty("rowVersion").GetString()!)}";
}
