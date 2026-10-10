using System.Globalization;
using System.Net;
using System.Text.Json;
using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Domain.Auditing;
using EnterpriseInventory.Infrastructure.Persistence;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseInventory.IntegrationTests.Assets;

/// <summary>
/// Day 30: many administrators working on the same assets at the same moment, through the whole API on SQL Server.
/// Whatever wins a race, no change is lost or half-saved, an asset never has two holders, every committed change has
/// exactly one audit record, and no request fails with a 500. Where timing matters, the requests are held just before
/// their save until all of them have read the same version (<see cref="SaveGate"/>), so the race really happens
/// instead of running one by one. These tests found that assignments to the same person and assignments racing with
/// returns could fail with a 500 or a wrong 409; see AssetAssignmentStore for the locks that fixed it.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class ConcurrencyTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime, IDisposable
{
    private const string User = "ayse.admin";

    /// <summary>What a request that lost a race hears: the row version or the one-active-assignment index stopped it.</summary>
    private static readonly string[] LosingCodes = ["concurrency_conflict", "asset_already_assigned"];

    private readonly FakeEmployees _people = new(4);
    private readonly SaveGate _gate = new();
    private string _connectionString = string.Empty;
    private TestApiFactory? _api;
    private InventoryReferences _refs = null!;

    public async Task InitializeAsync()
    {
        if (SqlServerDatabaseFixture.ServerConnectionString is null)
        {
            return;
        }

        _connectionString = await fixture.SharedAsync("concurrency", fixture.CreateMigratedDatabaseAsync);
        _refs = await InventoryReferences.SeedAsync(fixture, _connectionString);
        _api = new TestApiFactory(
            _connectionString,
            settings: _people.Settings(),
            configureServices: services => services.ConfigureDbContext<ApplicationDbContext>(options => options.AddInterceptors(_gate)));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _api?.Dispose();

    [SqlServerFact]
    public async Task Of_eight_simultaneous_edits_of_one_version_exactly_one_is_saved_whole()
    {
        using var client = _api!.CreateSignedInClient(User);
        var asset = await client.CreateAssetAsync(_refs.NewAssetBody());

        _gate.HoldUntil(8, AssetIsSaved);
        var responses = await Task.WhenAll(Enumerable.Range(1, 8).Select(racer =>
        {
            var edit = asset.ToUpdateBody();
            edit["description"] = $"Yarışçı {racer}";
            edit["computerName"] = $"PC-YARIS-{racer}";
            return SendAsync(HttpMethod.Put, $"/api/assets/{asset.Id()}", edit);
        }));

        Assert.True(_gate.Released, "The requests never all reached their save together.");
        var winner = AssertOneWinner(responses, HttpStatusCode.OK);
        Assert.All(responses.Where(r => r != winner), r => Assert.Equal("concurrency_conflict", r.Code));

        // The winner's change is stored whole: both of its fields, none of a loser's.
        await using var db = fixture.CreateContextFor(_connectionString);
        var saved = await db.Assets.SingleAsync(a => a.Id == asset.Id());
        var racerName = winner.Body.GetProperty("description").GetString();
        Assert.Equal(racerName, saved.Description);
        Assert.Equal($"PC-YARIS-{racerName!["Yarışçı ".Length..]}", saved.ComputerName);
        Assert.Equal([AuditAction.Created, AuditAction.Updated], await AuditActionsAsync(db, asset.Id()));
    }

    [SqlServerFact]
    public async Task Different_changes_racing_on_one_version_leave_exactly_one_of_them()
    {
        using var client = _api!.CreateSignedInClient(User);
        var employee = await client.EmployeeGuidAsync(_people.First);

        // A few rounds, so different kinds of change get to win.
        for (var round = 0; round < 3; round++)
        {
            var asset = await client.CreateAssetAsync(_refs.NewAssetBody());
            var version = asset.GetProperty("rowVersion").GetString()!;
            var edit = asset.ToUpdateBody();
            edit["description"] = "Yarışan düzenleme";
            var racers = new (AuditAction Action, Func<Task<Outcome>> Send)[]
            {
                (AuditAction.Updated, () => SendAsync(HttpMethod.Put, $"/api/assets/{asset.Id()}", edit)),
                (AuditAction.LocationChanged, () => SendAsync(HttpMethod.Put, $"/api/assets/{asset.Id()}/location", new Dictionary<string, object?>
                {
                    ["cityId"] = _refs.SecondCityId,
                    ["departmentId"] = _refs.SecondDepartmentId,
                    ["locationId"] = null,
                    ["rowVersion"] = version,
                })),
                (AuditAction.Assigned, () => SendAsync(HttpMethod.Post, $"/api/assets/{asset.Id()}/assignments", asset.AssignBody(employee))),
                (AuditAction.Archived, () => SendAsync(HttpMethod.Delete, $"/api/assets/{asset.Id()}?rowVersion={Uri.EscapeDataString(version)}")),
            };

            _gate.HoldUntil(racers.Length, AssetIsSaved);
            var outcomes = await Task.WhenAll(racers.Select(r => r.Send()));

            Assert.True(_gate.Released, "The requests never all reached their save together.");
            var succeeded = outcomes.Select((outcome, i) => (outcome, racers[i].Action)).Where(x => x.outcome.Succeeded).ToList();
            var (_, won) = Assert.Single(succeeded);
            Assert.All(outcomes.Where(o => !o.Succeeded), o =>
            {
                Assert.Equal(HttpStatusCode.Conflict, o.Status);
                Assert.Contains(o.Code, LosingCodes);
            });

            await using var db = fixture.CreateContextFor(_connectionString);
            var saved = await db.Assets.IgnoreQueryFilters().SingleAsync(a => a.Id == asset.Id());
            var active = await db.AssetAssignments.CountAsync(x => x.AssetId == asset.Id() && x.ReturnedAt == null);
            Assert.Equal([AuditAction.Created, won], await AuditActionsAsync(db, asset.Id()));
            Assert.Equal(won == AuditAction.Updated ? "Yarışan düzenleme" : "Test demirbaşı", saved.Description);
            Assert.Equal(won == AuditAction.LocationChanged ? _refs.SecondCityId : _refs.CityId, saved.CityId);
            Assert.Equal(won == AuditAction.Assigned ? 1 : 0, active);
            Assert.Equal(won == AuditAction.Assigned ? AssetStatus.Assigned : AssetStatus.Available, saved.Status);
            Assert.Equal(won == AuditAction.Archived, saved.IsDeleted);
        }
    }

    [SqlServerFact]
    public async Task Ten_assets_wanted_by_four_employees_at_once_each_get_exactly_one_holder()
    {
        using var client = _api!.CreateSignedInClient(User);
        var employees = new List<Guid>();
        foreach (var name in _people.Names)
        {
            employees.Add(await client.EmployeeGuidAsync(name));
        }

        var assets = new List<JsonElement>();
        for (var i = 0; i < 10; i++)
        {
            assets.Add(await client.CreateAssetAsync(_refs.NewAssetBody()));
        }

        // Forty requests at once, with no gate: the order is whatever the server makes of it.
        var outcomes = await Task.WhenAll(
            from asset in assets
            from employee in employees
            select SendAsync(HttpMethod.Post, $"/api/assets/{asset.Id()}/assignments", asset.AssignBody(employee)).ContinueWith(t => (Asset: asset.Id(), Outcome: t.Result), TaskScheduler.Default));

        await using var db = fixture.CreateContextFor(_connectionString);
        foreach (var asset in assets)
        {
            var mine = outcomes.Where(o => o.Asset == asset.Id()).Select(o => o.Outcome).ToList();
            var winner = Assert.Single(mine, o => o.Status == HttpStatusCode.Created);
            Assert.All(mine.Where(o => o != winner), o => Assert.Contains(o.Code, LosingCodes));

            var holder = await db.AssetAssignments.Include(x => x.Employee).SingleAsync(x => x.AssetId == asset.Id());
            Assert.Null(holder.ReturnedAt);
            Assert.Equal(winner.Body.GetProperty("activeAssignment").GetProperty("userName").GetString(), holder.Employee.SamAccountName);
            Assert.Equal(AssetStatus.Assigned, (await db.Assets.SingleAsync(a => a.Id == asset.Id())).Status);
            Assert.Equal([AuditAction.Created, AuditAction.Assigned], await AuditActionsAsync(db, asset.Id()));
        }
    }

    [SqlServerFact]
    public async Task Twelve_assets_given_to_one_new_employee_at_once_are_all_assigned()
    {
        using var client = _api!.CreateSignedInClient(User);
        var employee = await client.EmployeeGuidAsync(_people.First);
        var assets = new List<JsonElement>();
        for (var i = 0; i < 12; i++)
        {
            assets.Add(await client.CreateAssetAsync(_refs.NewAssetBody()));
        }

        // Different assets, so nothing about the assets stands between the requests; they share only the person's
        // record, which does not exist yet: every request finds it missing and wants to add it.
        var outcomes = await Task.WhenAll(assets.Select(asset => SendAsync(HttpMethod.Post, $"/api/assets/{asset.Id()}/assignments", asset.AssignBody(employee))));

        Assert.All(outcomes, o => Assert.Equal(HttpStatusCode.Created, o.Status));
        await using var db = fixture.CreateContextFor(_connectionString);
        var record = await db.Employees.SingleAsync(e => e.ObjectGuid == employee);
        var ids = assets.ConvertAll(a => a.Id());
        Assert.Equal(12, await db.AssetAssignments.CountAsync(x => ids.Contains(x.AssetId) && x.EmployeeId == record.Id && x.ReturnedAt == null));
    }

    [SqlServerFact]
    public async Task Assignments_and_returns_racing_for_one_asset_keep_every_committed_change_and_a_consistent_history()
    {
        using var client = _api!.CreateSignedInClient(User);
        var asset = await client.CreateAssetAsync(_refs.NewAssetBody());
        var employees = new List<Guid>();
        foreach (var name in _people.Names)
        {
            employees.Add(await client.EmployeeGuidAsync(name));
        }

        // Four people take the asset and give it back as fast as they can; most attempts lose to someone else.
        var counts = await Task.WhenAll(employees.Select(async employee =>
        {
            var (assigned, returned) = (0, 0);
            using var worker = _api.CreateSignedInClient(User);
            for (var step = 0; step < 8; step++)
            {
                var current = await worker.GetOkAsync($"/api/assets/{asset.Id()}");
                var version = current.GetProperty("rowVersion").GetString();
                if (current.GetProperty("status").GetString() == "Available")
                {
                    using var response = await worker.PostAssignmentAsync(asset.Id(), current.AssignBody(employee));
                    assigned += Count(response, HttpStatusCode.Created);
                }
                else
                {
                    using var response = await worker.PostReturnAsync(asset.Id(), version);
                    returned += Count(response, HttpStatusCode.OK);
                }
            }

            return (assigned, returned);
        }));

        var assignedTotal = counts.Sum(c => c.assigned);
        var returnedTotal = counts.Sum(c => c.returned);
        Assert.True(assignedTotal > 0, "No assignment succeeded; the race did not happen.");

        await using var db = fixture.CreateContextFor(_connectionString);
        var periods = await db.AssetAssignments.Where(x => x.AssetId == asset.Id()).OrderBy(x => x.AssignedAt).ThenBy(x => x.Id).ToListAsync();
        var saved = await db.Assets.SingleAsync(a => a.Id == asset.Id());
        var audit = await AuditActionsAsync(db, asset.Id());

        // Every success a client was told about is stored, and nothing else is.
        Assert.Equal(assignedTotal, periods.Count);
        Assert.Equal(returnedTotal, periods.Count(p => p.ReturnedAt is not null));
        Assert.Equal(assignedTotal, audit.Count(a => a == AuditAction.Assigned));
        Assert.Equal(returnedTotal, audit.Count(a => a == AuditAction.Returned));

        // One holder at a time: periods follow each other, and only the last can still be open.
        Assert.True(periods.Count(p => p.ReturnedAt is null) <= 1);
        for (var i = 1; i < periods.Count; i++)
        {
            Assert.NotNull(periods[i - 1].ReturnedAt);
            Assert.True(periods[i - 1].ReturnedAt <= periods[i].AssignedAt, $"Period {i} starts before period {i - 1} ended.");
        }

        Assert.Equal(periods.LastOrDefault() is { ReturnedAt: null } ? AssetStatus.Assigned : AssetStatus.Available, saved.Status);
    }

    [SqlServerFact]
    public async Task Six_simultaneous_creates_with_one_asset_code_store_one_asset()
    {
        var code = PersistenceTestData.Unique("YRS")[..30];

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => SendAsync(HttpMethod.Post, "/api/assets", _refs.NewAssetBody(code))));

        Assert.Single(outcomes, o => o.Status == HttpStatusCode.Created);
        Assert.All(outcomes.Where(o => o.Status != HttpStatusCode.Created), o =>
        {
            Assert.Equal(HttpStatusCode.Conflict, o.Status);
            Assert.Equal("duplicate_value", o.Code);
        });
        await using var db = fixture.CreateContextFor(_connectionString);
        Assert.Equal(1, await db.Assets.IgnoreQueryFilters().CountAsync(a => a.AssetCode == code));
    }

    // --- Helpers ------------------------------------------------------------------------------------------------

    /// <summary>What one racer heard back.</summary>
    private sealed record Outcome(HttpStatusCode Status, JsonElement Body)
    {
        public bool Succeeded => (int)Status is >= 200 and < 300;

        public string? Code => Body.ValueKind == JsonValueKind.Object && Body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>A save that changes an asset row: every update, move, assignment, return and archive makes one.</summary>
    private static bool AssetIsSaved(DbContext context) =>
        context.ChangeTracker.Entries<Asset>().Any(e => e.State == EntityState.Modified);

    /// <summary>Sends as its own browser would: its own client and CSRF token.</summary>
    private async Task<Outcome> SendAsync(HttpMethod method, string path, object? body = null)
    {
        using var racer = _api!.CreateSignedInClient(User);
        using var response = await racer.SendWithCsrfAsync(method, path, body);
        var text = await response.Content.ReadAsStringAsync();
        return new Outcome(response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static Outcome AssertOneWinner(Outcome[] outcomes, HttpStatusCode success)
    {
        var winner = Assert.Single(outcomes, o => o.Status == success);
        Assert.All(outcomes.Where(o => o != winner), o => Assert.Equal(HttpStatusCode.Conflict, o.Status));
        return winner;
    }

    /// <summary>1 for the expected success; 0 for a lost race (409). Anything else fails the test.</summary>
    private static int Count(HttpResponseMessage response, HttpStatusCode success)
    {
        Assert.True(response.StatusCode == success || response.StatusCode == HttpStatusCode.Conflict, $"Unexpected {(int)response.StatusCode}");
        return response.StatusCode == success ? 1 : 0;
    }

    private static async Task<List<AuditAction>> AuditActionsAsync(ApplicationDbContext db, int assetId)
    {
        var entityId = assetId.ToString(CultureInfo.InvariantCulture);
        return await db.AuditLogs.Where(l => l.EntityName == nameof(Asset) && l.EntityId == entityId).OrderBy(l => l.Id).Select(l => l.Action).ToListAsync();
    }
}
