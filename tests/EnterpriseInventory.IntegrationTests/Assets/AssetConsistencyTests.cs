using System.Globalization;
using System.Net;
using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Domain.Auditing;
using EnterpriseInventory.Infrastructure.Persistence;
using EnterpriseInventory.Infrastructure.Persistence.Interceptors;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseInventory.IntegrationTests.Assets;

/// <summary>
/// What happens when a write cannot finish, on a database of its own (a test constraint is added to it):
/// <list type="bullet">
/// <item>Another DbContext saves the same asset after the API read it and before the API saves: the database
/// refuses the API's save on the row version, the caller gets 409, and the other change is kept.</item>
/// <item>The audit record cannot be written: the change and the audit record are rolled back together.</item>
/// </list>
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class AssetConsistencyTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime, IDisposable
{
    private const string User = "ayse.admin";
    private const string OtherUser = "mehmet.admin";

    /// <summary>The test constraint refuses every audit record written in this user's name.</summary>
    private const string AuditFailsUser = "audit.fails";

    private readonly ConcurrentEditInterceptor _concurrentEdit = new();
    private string _connectionString = string.Empty;
    private TestApiFactory? _api;
    private InventoryReferences _refs = null!;

    public async Task InitializeAsync()
    {
        if (SqlServerDatabaseFixture.ServerConnectionString is null)
        {
            return;
        }

        _connectionString = await fixture.CreateMigratedDatabaseAsync();
        _refs = await InventoryReferences.SeedAsync(fixture, _connectionString);
        await using (var db = fixture.CreateContextFor(_connectionString))
        {
            // Fails inside SQL Server, after the asset change in the same transaction has already run. (A check
            // constraint rather than a trigger: EF Core's INSERT ... OUTPUT cannot be used on a table with triggers.)
            await db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE [AuditLogs] ADD CONSTRAINT [CK_Test_AuditFails] CHECK ([UserName] <> N'{AuditFailsUser}')");
        }

        _concurrentEdit.Use(_connectionString);
        _api = new TestApiFactory(
            _connectionString,
            settings: new Dictionary<string, string?> { ["RateLimiting:PermitLimit"] = "1000" },
            configureServices: services => services.ConfigureDbContext<ApplicationDbContext>(options => options.AddInterceptors(_concurrentEdit)));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _api?.Dispose();

    [SqlServerFact]
    public async Task An_update_saved_by_another_context_between_read_and_save_gets_409_and_keeps_the_other_change()
    {
        using var client = _api!.CreateSignedInClient(User);
        var created = await client.CreateAssetAsync(_refs.NewAssetBody());
        var body = created.ToUpdateBody();
        body["description"] = "Ayşe'nin düzenlemesi";
        _concurrentEdit.EditBeforeTheNextSaveOf(created.Id());

        using var response = await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{created.Id()}", body);

        Assert.Equal("concurrency_conflict", (await AssetApi.ReadAsync(response, HttpStatusCode.Conflict)).GetProperty("code").GetString());
        Assert.True(_concurrentEdit.Edited);
        var current = await client.GetOkAsync($"/api/assets/{created.Id()}");
        Assert.Equal(ConcurrentEditInterceptor.Description, current.GetProperty("description").GetString());
        Assert.Equal(OtherUser, current.GetProperty("updatedBy").GetString());
        Assert.Equal([AuditAction.Created], await AuditActionsAsync(created.Id()));

        // The caller reloads and saves again on the current version.
        var retry = current.ToUpdateBody();
        retry["description"] = "Ayşe'nin düzenlemesi";
        using var retried = await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{created.Id()}", retry);
        Assert.Equal("Ayşe'nin düzenlemesi", (await AssetApi.ReadAsync(retried, HttpStatusCode.OK)).GetProperty("description").GetString());
    }

    [SqlServerFact]
    public async Task An_archive_after_another_context_saved_the_asset_gets_409_and_leaves_it_active()
    {
        using var client = _api!.CreateSignedInClient(User);
        var created = await client.CreateAssetAsync(_refs.NewAssetBody());
        _concurrentEdit.EditBeforeTheNextSaveOf(created.Id());

        using var response = await client.SendWithCsrfAsync(HttpMethod.Delete, ArchivePath(created.Id(), created.GetProperty("rowVersion").GetString()!));

        Assert.Equal("concurrency_conflict", (await AssetApi.ReadAsync(response, HttpStatusCode.Conflict)).GetProperty("code").GetString());
        Assert.True(_concurrentEdit.Edited);
        var current = await client.GetOkAsync($"/api/assets/{created.Id()}");
        Assert.False(current.GetProperty("isArchived").GetBoolean());
        Assert.Equal(ConcurrentEditInterceptor.Description, current.GetProperty("description").GetString());
        Assert.Equal([AuditAction.Created], await AuditActionsAsync(created.Id()));
    }

    [SqlServerFact]
    public async Task A_create_whose_audit_record_fails_leaves_no_asset_behind()
    {
        using var client = _api!.CreateSignedInClient(AuditFailsUser);
        var body = _refs.NewAssetBody();

        using var response = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/assets", body);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await using var db = fixture.CreateContextFor(_connectionString);
        var code = (string)body["assetCode"]!;
        Assert.False(await db.Assets.IncludingArchived().AnyAsync(a => a.AssetCode == code));
        Assert.False(await db.AuditLogs.AnyAsync(l => l.UserName == AuditFailsUser));
    }

    [SqlServerFact]
    public async Task An_update_whose_audit_record_fails_changes_nothing()
    {
        using var owner = _api!.CreateSignedInClient(User);
        var created = await owner.CreateAssetAsync(_refs.NewAssetBody());
        using var client = _api.CreateSignedInClient(AuditFailsUser);
        var body = created.ToUpdateBody();
        body["description"] = "Kaydedilmemeli";
        body["status"] = "Faulty";

        using var response = await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{created.Id()}", body);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var current = await owner.GetOkAsync($"/api/assets/{created.Id()}");
        Assert.Equal(created.GetProperty("rowVersion").GetString(), current.GetProperty("rowVersion").GetString());
        Assert.Equal(created.GetProperty("description").GetString(), current.GetProperty("description").GetString());
        Assert.Equal("Available", current.GetProperty("status").GetString());
        Assert.Equal([AuditAction.Created], await AuditActionsAsync(created.Id()));
    }

    [SqlServerFact]
    public async Task An_archive_whose_audit_record_fails_leaves_the_asset_in_the_list()
    {
        using var owner = _api!.CreateSignedInClient(User);
        var created = await owner.CreateAssetAsync(_refs.NewAssetBody());
        using var client = _api.CreateSignedInClient(AuditFailsUser);

        using var response = await client.SendWithCsrfAsync(HttpMethod.Delete, ArchivePath(created.Id(), created.GetProperty("rowVersion").GetString()!));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var current = await owner.GetOkAsync($"/api/assets/{created.Id()}");
        Assert.False(current.GetProperty("isArchived").GetBoolean());
        Assert.Equal(created.GetProperty("rowVersion").GetString(), current.GetProperty("rowVersion").GetString());
        Assert.Contains(created.GetProperty("assetCode").GetString(), (await owner.GetOkAsync("/api/assets?pageSize=100")).Codes());
        Assert.Equal([AuditAction.Created], await AuditActionsAsync(created.Id()));
    }

    private static string ArchivePath(int id, string rowVersion) => $"/api/assets/{id}?rowVersion={Uri.EscapeDataString(rowVersion)}";

    private async Task<List<AuditAction>> AuditActionsAsync(int assetId)
    {
        await using var db = fixture.CreateContextFor(_connectionString);
        var entityId = assetId.ToString(CultureInfo.InvariantCulture);
        return await db.AuditLogs.Where(l => l.EntityName == nameof(Asset) && l.EntityId == entityId).OrderBy(l => l.Id).Select(l => l.Action).ToListAsync();
    }

    /// <summary>
    /// When armed, saves a change to the asset through a second, separate DbContext just before the API's own
    /// context saves it: the API has read and checked the old row version, and the database now has a newer one.
    /// </summary>
    private sealed class ConcurrentEditInterceptor : SaveChangesInterceptor
    {
        public const string Description = "Başka bir kullanıcının değişikliği";

        private string _connectionString = string.Empty;
        private int? _assetId;

        public bool Edited { get; private set; }

        public void Use(string connectionString) => _connectionString = connectionString;

        public void EditBeforeTheNextSaveOf(int assetId) => (_assetId, Edited) = (assetId, false);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_assetId is { } id
                && eventData.Context!.ChangeTracker.Entries<Asset>().Any(e => e.Entity.Id == id && e.State == EntityState.Modified))
            {
                _assetId = null;
                // Stamped with the real clock, like the API's own saves (the fixture's test clock is in the past).
                var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseSqlServer(_connectionString)
                    .AddInterceptors(new AuditableEntityInterceptor(new TestCurrentUser(OtherUser), TimeProvider.System))
                    .Options;
                await using var other = new ApplicationDbContext(options);
                var asset = await other.Assets.SingleAsync(a => a.Id == id, cancellationToken);
                asset.UpdateDetails(asset.AssetCode, asset.AssetType, asset.ComputerName, asset.SerialNumber, Description);
                await other.SaveChangesAsync(cancellationToken);
                Edited = true;
            }

            return result;
        }
    }
}
