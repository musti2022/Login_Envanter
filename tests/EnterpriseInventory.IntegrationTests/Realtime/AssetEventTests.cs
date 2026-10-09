using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using EnterpriseInventory.Api.Realtime;
using EnterpriseInventory.Application.Assets;
using EnterpriseInventory.Domain.Auditing;
using EnterpriseInventory.Infrastructure.Persistence;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Assets;
using EnterpriseInventory.IntegrationTests.Persistence;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseInventory.IntegrationTests.Realtime;

/// <summary>
/// Day 27: every committed change to an asset reaches the open hub connections, only after the commit; refused,
/// unchanged and rolled-back writes are not announced; and a failing notifier never fails or undoes a committed
/// change. Administrators sign in with the API's own cookie through the Development fake directory.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class AssetEventTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    private const string AdminPassword = "fake-admin-password";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private static readonly string[] Events =
        ["AssetCreated", "AssetUpdated", "AssetArchived", "AssetAssigned", "AssetReturned", "AssetLocationChanged"];

    private readonly FakeEmployees _people = new();
    private readonly string _admin = "y" + Guid.NewGuid().ToString("N")[..10];
    private readonly string _secondAdmin = "y" + Guid.NewGuid().ToString("N")[..10];
    private readonly FailingMoves _failingMoves = new();
    private readonly List<IAsyncDisposable> _resources = [];
    private TestApiFactory _api = null!;
    private InventoryReferences _refs = null!;

    public async Task InitializeAsync()
    {
        if (SqlServerDatabaseFixture.ServerConnectionString is null)
        {
            return;
        }

        _refs = await InventoryReferences.SeedAsync(fixture);
        _api = Api();
    }

    public async Task DisposeAsync()
    {
        _resources.Reverse();
        foreach (var resource in _resources)
        {
            await resource.DisposeAsync();
        }
    }

    [SqlServerFact]
    public async Task Every_kind_of_change_is_announced_once_it_is_committed()
    {
        var (browser, heard) = await ListeningAdminAsync(_admin);
        var client = browser.Http;
        var employee = await client.EmployeeGuidAsync(_people.First);

        var asset = await client.CreateAssetAsync(_refs.NewAssetBody());
        await AssertHeardAsync(heard, "AssetCreated", asset, AuditAction.Created);

        var edit = asset.ToUpdateBody();
        edit["description"] = "Canlı bildirim testi";
        asset = await ReadAsync(await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{asset.Id()}", edit), HttpStatusCode.OK);
        await AssertHeardAsync(heard, "AssetUpdated", asset, AuditAction.Updated);

        asset = await ReadAsync(await client.PutLocationAsync(asset.Id(), Move(asset, _refs.SecondCityId, _refs.SecondDepartmentId, _refs.SecondCityLocationId)), HttpStatusCode.OK);
        await AssertHeardAsync(heard, "AssetLocationChanged", asset, AuditAction.LocationChanged);

        asset = await client.AssignAsync(asset, employee);
        await AssertHeardAsync(heard, "AssetAssigned", asset, AuditAction.Assigned);

        asset = await client.ReturnAsync(asset);
        await AssertHeardAsync(heard, "AssetReturned", asset, AuditAction.Returned);

        using (var archive = await client.SendWithCsrfAsync(HttpMethod.Delete, $"/api/assets/{asset.Id()}?rowVersion={Uri.EscapeDataString(asset.GetProperty("rowVersion").GetString()!)}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        }

        asset = await client.GetOkAsync($"/api/assets/{asset.Id()}");
        await AssertHeardAsync(heard, "AssetArchived", asset, AuditAction.Archived);
    }

    [SqlServerFact]
    public async Task Every_open_connection_hears_of_a_change_made_by_another_administrator()
    {
        var (first, firstHeard) = await ListeningAdminAsync(_admin);
        var (_, secondHeard) = await ListeningAdminAsync(_secondAdmin);
        var (_, secondTabHeard) = await ListeningAdminAsync(_secondAdmin);

        var asset = await first.Http.CreateAssetAsync(_refs.NewAssetBody());

        foreach (var heard in new[] { firstHeard, secondHeard, secondTabHeard })
        {
            var notice = await NextAsync(heard);
            Assert.Equal(("AssetCreated", asset.Id()), (notice.Event, notice.AssetId));
        }
    }

    [SqlServerFact]
    public async Task Refused_and_unchanged_writes_are_not_announced()
    {
        var (browser, heard) = await ListeningAdminAsync(_admin);
        var client = browser.Http;
        var asset = await client.CreateAssetAsync(_refs.NewAssetBody());
        Assert.Equal("AssetCreated", (await NextAsync(heard)).Event);
        var stale = asset.ToUpdateBody();
        var edit = asset.ToUpdateBody();
        edit["description"] = "Yeni açıklama";
        var changed = await ReadAsync(await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{asset.Id()}", edit), HttpStatusCode.OK);
        Assert.Equal("AssetUpdated", (await NextAsync(heard)).Event);

        // A stale version, invalid input, a rule, an unknown asset, and writes that change nothing.
        stale["description"] = "Eski sürümden";
        await ExpectAsync(client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{asset.Id()}", stale), HttpStatusCode.Conflict);
        var invalid = changed.ToUpdateBody();
        invalid["assetCode"] = string.Empty;
        await ExpectAsync(client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{asset.Id()}", invalid), HttpStatusCode.BadRequest);
        await ExpectAsync(client.PostReturnAsync(asset.Id(), changed.GetProperty("rowVersion").GetString()), HttpStatusCode.Conflict);
        await ExpectAsync(client.SendWithCsrfAsync(HttpMethod.Put, "/api/assets/999999999", changed.ToUpdateBody()), HttpStatusCode.NotFound);
        await ExpectAsync(client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{asset.Id()}", changed.ToUpdateBody()), HttpStatusCode.OK);
        await ExpectAsync(client.PutLocationAsync(asset.Id(), Move(changed, _refs.CityId, _refs.DepartmentId, _refs.LocationId)), HttpStatusCode.OK);

        // Notifications keep their order, so the next one is the next real change.
        var moved = await ReadAsync(await client.PutLocationAsync(asset.Id(), Move(changed, _refs.SecondCityId, _refs.SecondDepartmentId, null)), HttpStatusCode.OK);
        await AssertHeardAsync(heard, "AssetLocationChanged", moved, AuditAction.LocationChanged);
    }

    [SqlServerFact]
    public async Task A_change_that_is_rolled_back_is_not_announced()
    {
        var (browser, heard) = await ListeningAdminAsync(_admin);
        var client = browser.Http;
        var asset = await client.CreateAssetAsync(_refs.NewAssetBody());
        Assert.Equal("AssetCreated", (await NextAsync(heard)).Event);

        // The move's audit record cannot be saved: the transaction is rolled back and the request fails.
        _failingMoves.Enabled = true;
        await ExpectAsync(client.PutLocationAsync(asset.Id(), Move(asset, _refs.SecondCityId, _refs.SecondDepartmentId, null)), HttpStatusCode.InternalServerError);
        _failingMoves.Enabled = false;
        Assert.Equal(asset.GetProperty("rowVersion").GetString(), (await client.GetOkAsync($"/api/assets/{asset.Id()}")).GetProperty("rowVersion").GetString());

        var moved = await ReadAsync(await client.PutLocationAsync(asset.Id(), Move(asset, _refs.SecondCityId, _refs.SecondDepartmentId, null)), HttpStatusCode.OK);
        await AssertHeardAsync(heard, "AssetLocationChanged", moved, AuditAction.LocationChanged);
    }

    [SqlServerFact]
    public async Task A_failing_notifier_neither_fails_nor_undoes_a_committed_change()
    {
        await using var api = new TestApiFactory(
            fixture.ConnectionString,
            settings: _people.Settings(),
            configureServices: services => services.AddSingleton<IAssetChangeNotifier, ThrowingNotifier>());
        using var client = api.CreateSignedInClient("ayse.admin");

        var asset = await client.CreateAssetAsync(_refs.NewAssetBody());
        var edit = asset.ToUpdateBody();
        edit["description"] = "Bildirim olmadan kaydedildi";
        var updated = await ReadAsync(await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{asset.Id()}", edit), HttpStatusCode.OK);

        Assert.Equal("Bildirim olmadan kaydedildi", (await client.GetOkAsync($"/api/assets/{asset.Id()}")).GetProperty("description").GetString());
        Assert.Equal(updated.GetProperty("rowVersion").GetString(), (await client.GetOkAsync($"/api/assets/{asset.Id()}")).GetProperty("rowVersion").GetString());
        await using var db = fixture.CreateContext();
        var entityId = asset.Id().ToString(CultureInfo.InvariantCulture);
        Assert.Equal(
            [AuditAction.Created, AuditAction.Updated],
            await db.AuditLogs.Where(l => l.EntityName == "Asset" && l.EntityId == entityId).OrderBy(l => l.Id).Select(l => l.Action).ToListAsync());
    }

    // --- Helpers ------------------------------------------------------------------------------------------------

    /// <summary>What a connection heard, and what the database showed to a reader that refuses to wait for locks.</summary>
    private sealed record Heard(string Event, int AssetId, DateTimeOffset OccurredAt, string? CommittedVersion, AuditAction? LastAudit, string? Blocked);

    private TestApiFactory Api()
    {
        var settings = _people.Settings();
        var next = _people.Names.Count + 1;
        foreach (var admin in new[] { _admin, _secondAdmin })
        {
            settings[$"ActiveDirectory:FakeUsers:{next}:UserName"] = admin;
            settings[$"ActiveDirectory:FakeUsers:{next}:Password"] = AdminPassword;
            settings[$"ActiveDirectory:FakeUsers:{next}:DisplayName"] = $"Yönetici {admin}";
            settings[$"ActiveDirectory:FakeUsers:{next}:IsAllowedGroupMember"] = "true";
            next++;
        }

        var api = new TestApiFactory(
            fixture.ConnectionString,
            settings: settings,
            useTestAuthentication: false,
            configureServices: services => services.ConfigureDbContext<ApplicationDbContext>(options => options.AddInterceptors(_failingMoves)));
        _resources.Add(api);
        return api;
    }

    /// <summary>An administrator's browser with an open connection that records each notification as it arrives.</summary>
    private async Task<(LiveBrowser Browser, ChannelReader<Heard> Heard)> ListeningAdminAsync(string userName)
    {
        var browser = new LiveBrowser(_api);
        _resources.Add(browser);
        await browser.SignInAsync(userName, AdminPassword);
        var heard = Channel.CreateUnbounded<Heard>();
        var hub = browser.Hub();
        foreach (var name in Events)
        {
            hub.On<AssetNotification>(name, async notification =>
                await heard.Writer.WriteAsync(await ObserveAsync(name, notification)));
        }

        await hub.StartAsync();
        return (browser, heard.Reader);
    }

    /// <summary>
    /// Reads the asset the moment the notification arrives, without waiting for locks: a transaction still open on the
    /// asset would make the read fail instead of quietly waiting for the commit.
    /// </summary>
    private async Task<Heard> ObserveAsync(string name, AssetNotification notification)
    {
        await using var db = fixture.CreateContext();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 0");
        try
        {
            var version = await db.Assets.IgnoreQueryFilters().AsNoTracking()
                .Where(a => a.Id == notification.AssetId).Select(a => a.RowVersion).SingleAsync();
            var entityId = notification.AssetId.ToString(CultureInfo.InvariantCulture);
            var lastAudit = await db.AuditLogs.AsNoTracking()
                .Where(l => l.EntityName == "Asset" && l.EntityId == entityId).OrderByDescending(l => l.Id).Select(l => (AuditAction?)l.Action).FirstAsync();
            return new Heard(name, notification.AssetId, notification.OccurredAt, Convert.ToBase64String(version), lastAudit, null);
        }
        catch (SqlException ex) when (ex.Number == 1222)
        {
            return new Heard(name, notification.AssetId, notification.OccurredAt, null, null, ex.Message);
        }
    }

    private static async Task AssertHeardAsync(ChannelReader<Heard> heard, string expectedEvent, JsonElement asset, AuditAction expectedAudit)
    {
        var notice = await NextAsync(heard);
        Assert.Equal((expectedEvent, asset.Id()), (notice.Event, notice.AssetId));
        Assert.Null(notice.Blocked);

        // When the notification arrived, the change and its audit record were already committed.
        Assert.Equal(asset.GetProperty("rowVersion").GetString(), notice.CommittedVersion);
        Assert.Equal(expectedAudit, notice.LastAudit);
        var updatedAt = asset.GetProperty("updatedAt");
        var expectedTime = updatedAt.ValueKind == JsonValueKind.Null ? asset.GetProperty("createdAt").GetDateTimeOffset() : updatedAt.GetDateTimeOffset();
        Assert.Equal(expectedTime, notice.OccurredAt);
    }

    private static async Task<Heard> NextAsync(ChannelReader<Heard> heard) => await heard.ReadAsync().AsTask().WaitAsync(Patience);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            return await AssetApi.ReadAsync(response, expected);
        }
    }

    private static async Task ExpectAsync(Task<HttpResponseMessage> sending, HttpStatusCode expected) => await ReadAsync(await sending, expected);

    private static Dictionary<string, object?> Move(JsonElement asset, int cityId, int departmentId, int? locationId) => new()
    {
        ["cityId"] = cityId,
        ["departmentId"] = departmentId,
        ["locationId"] = locationId,
        ["rowVersion"] = asset.GetProperty("rowVersion").GetString(),
    };

    /// <summary>While enabled, a save that adds a move's audit record fails, so the move is rolled back.</summary>
    private sealed class FailingMoves : SaveChangesInterceptor
    {
        public volatile bool Enabled;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context!.ChangeTracker.Entries<AuditLog>().Any(e => e.State == EntityState.Added && e.Entity.Action == AuditAction.LocationChanged))
            {
                throw new DbUpdateException("Audit kaydı yazılamadı (test).");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class ThrowingNotifier : IAssetChangeNotifier
    {
        public void Notify(AssetChanged change) => throw new InvalidOperationException("Bildirim kanalı çalışmıyor (test).");
    }
}
