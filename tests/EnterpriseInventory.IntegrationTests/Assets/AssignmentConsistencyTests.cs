using System.Globalization;
using System.Net;
using System.Text.Json;
using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Domain.Auditing;
using EnterpriseInventory.Domain.Employees;
using EnterpriseInventory.Infrastructure.Persistence;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseInventory.IntegrationTests.Assets;

/// <summary>
/// Assignments and returns under concurrent requests and failures (days 22, 23 and 30), on a database of their own:
/// <list type="bullet">
/// <item>Requests that all read the same version of the asset and pass every check are held until all of them are
/// about to save, then released together: exactly one assignment (or return) is committed.</item>
/// <item>An active assignment written behind the API's back between its read and its save: the filtered unique index
/// refuses the API's assignment.</item>
/// <item>The audit record cannot be written: the assignment, return or move is rolled back with it.</item>
/// </list>
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class AssignmentConsistencyTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime, IDisposable
{
    private const string User = "ayse.admin";
    private const string AuditFailsUser = "audit.fails";
    private const int Racers = 8;

    /// <summary>What a request that lost the race hears: the row version or the unique index stopped it.</summary>
    private static readonly string[] LosingCodes = ["concurrency_conflict", "asset_already_assigned"];

    private readonly FakeEmployees _people = new(Racers);
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

        _connectionString = await fixture.SharedAsync("assignment-consistency", async () =>
        {
            var connectionString = await fixture.CreateMigratedDatabaseAsync();
            await using var db = fixture.CreateContextFor(connectionString);
            await db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE [AuditLogs] ADD CONSTRAINT [CK_Test_AuditFails] CHECK ([UserName] <> N'{AuditFailsUser}')");
            return connectionString;
        });
        _refs = await InventoryReferences.SeedAsync(fixture, _connectionString);
        _api = new TestApiFactory(
            _connectionString,
            settings: _people.Settings(),
            configureServices: services => services.ConfigureDbContext<ApplicationDbContext>(options => options.AddInterceptors(_gate)));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _api?.Dispose();

    [SqlServerFact]
    public async Task Of_eight_simultaneous_assignments_of_one_asset_exactly_one_is_committed()
    {
        using var client = _api!.CreateSignedInClient(User);
        var asset = await client.CreateAssetAsync(_refs.NewAssetBody());
        var employees = new List<Guid>();
        foreach (var name in _people.Names)
        {
            employees.Add(await client.EmployeeGuidAsync(name));
        }

        // Every request has read the asset at the same version and passed its checks before any of them saves.
        _gate.HoldUntil(Racers, context => context.ChangeTracker.Entries<AssetAssignment>().Any(e => e.State == EntityState.Added));
        var responses = await Task.WhenAll(employees.Select(async employee =>
        {
            using var racer = _api.CreateSignedInClient(User);
            using var response = await racer.PostAssignmentAsync(asset.Id(), asset.AssignBody(employee));
            return (response.StatusCode, Body: await response.Content.ReadAsStringAsync());
        }));

        Assert.True(_gate.Released, "The requests never all reached their save together.");
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r =>
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Contains(JsonDocument.Parse(r.Body).RootElement.GetProperty("code").GetString(), LosingCodes);
        });

        var winner = JsonDocument.Parse(responses.Single(r => r.StatusCode == HttpStatusCode.Created).Body).RootElement;
        await using var db = fixture.CreateContextFor(_connectionString);
        var active = await db.AssetAssignments.Where(x => x.AssetId == asset.Id() && x.ReturnedAt == null).Include(x => x.Employee).ToListAsync();
        Assert.Equal(winner.GetProperty("activeAssignment").GetProperty("userName").GetString(), Assert.Single(active).Employee.SamAccountName);
        Assert.Equal(AssetStatus.Assigned, (await db.Assets.SingleAsync(a => a.Id == asset.Id())).Status);
        Assert.Equal([AuditAction.Created, AuditAction.Assigned], await AuditActionsAsync(db, asset.Id()));
    }

    [SqlServerFact]
    public async Task Of_eight_simultaneous_returns_exactly_one_is_committed()
    {
        using var client = _api!.CreateSignedInClient(User);
        var assigned = await client.AssignAsync(await client.CreateAssetAsync(_refs.NewAssetBody()), await client.EmployeeGuidAsync(_people.First));

        _gate.HoldUntil(Racers, context => context.ChangeTracker.Entries<AssetAssignment>().Any(e => e.State == EntityState.Modified));
        var statuses = await Task.WhenAll(Enumerable.Range(0, Racers).Select(async _ =>
        {
            using var racer = _api.CreateSignedInClient(User);
            using var response = await racer.PostReturnAsync(assigned.Id(), assigned.GetProperty("rowVersion").GetString());
            return response.StatusCode;
        }));

        Assert.True(_gate.Released);
        Assert.Single(statuses, s => s == HttpStatusCode.OK);
        Assert.All(statuses.Where(s => s != HttpStatusCode.OK), s => Assert.Equal(HttpStatusCode.Conflict, s));
        await using var db = fixture.CreateContextFor(_connectionString);
        var period = await db.AssetAssignments.SingleAsync(x => x.AssetId == assigned.Id());
        Assert.NotNull(period.ReturnedAt);
        Assert.Equal(AssetStatus.Available, (await db.Assets.SingleAsync(a => a.Id == assigned.Id())).Status);
        Assert.Equal([AuditAction.Created, AuditAction.Assigned, AuditAction.Returned], await AuditActionsAsync(db, assigned.Id()));
    }

    [SqlServerFact]
    public async Task An_active_assignment_saved_behind_the_apis_back_makes_the_unique_index_refuse_the_apis_assignment()
    {
        using var client = _api!.CreateSignedInClient(User);
        var asset = await client.CreateAssetAsync(_refs.NewAssetBody());
        var employee = await client.EmployeeGuidAsync(_people.First);
        var other = Employee.Create(Guid.NewGuid(), "baska.kisi", "Başka Kişi", null, null, null, isActive: true, fixture.Clock.GetUtcNow());
        await using (var setup = fixture.CreateContextFor(_connectionString))
        {
            setup.Employees.Add(other);
            await setup.SaveChangesAsync();
        }

        // Written with plain SQL, as another application or a race the row version cannot see would: the asset row
        // is not touched, so only the filtered unique index stands between the two assignments.
        _gate.RunBefore(
            context => context.ChangeTracker.Entries<AssetAssignment>().Any(e => e.State == EntityState.Added),
            async () =>
            {
                await using var db = fixture.CreateContextFor(_connectionString);
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO [AssetAssignments] ([AssetId], [EmployeeId], [AssignmentDescription], [AssignedAt], [AssignedBy]) VALUES ({asset.Id()}, {other.Id}, N'Başka yoldan', SYSDATETIMEOFFSET(), N'baska.uygulama')");
            });

        using var response = await client.PostAssignmentAsync(asset.Id(), asset.AssignBody(employee));

        Assert.Equal("asset_already_assigned", (await AssetApi.ReadAsync(response, HttpStatusCode.Conflict)).Code());
        await using var check = fixture.CreateContextFor(_connectionString);
        var active = await check.AssetAssignments.Where(x => x.AssetId == asset.Id()).ToListAsync();
        Assert.Equal(other.Id, Assert.Single(active).EmployeeId);
        Assert.Equal([AuditAction.Created], await AuditActionsAsync(check, asset.Id()));
        Assert.False(await check.Employees.AnyAsync(e => e.ObjectGuid == employee), "The employee record added in the refused transaction was rolled back too.");
    }

    [SqlServerFact]
    public async Task An_assignment_whose_audit_record_fails_leaves_no_assignment_and_no_employee_record()
    {
        using var owner = _api!.CreateSignedInClient(User);
        var asset = await owner.CreateAssetAsync(_refs.NewAssetBody());
        var employee = await owner.EmployeeGuidAsync(_people.First);
        using var client = _api.CreateSignedInClient(AuditFailsUser);

        using var response = await client.PostAssignmentAsync(asset.Id(), asset.AssignBody(employee));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var current = await owner.GetOkAsync($"/api/assets/{asset.Id()}");
        Assert.Equal("Available", current.GetProperty("status").GetString());
        Assert.Equal(asset.GetProperty("rowVersion").GetString(), current.GetProperty("rowVersion").GetString());
        await using var db = fixture.CreateContextFor(_connectionString);
        Assert.False(await db.AssetAssignments.AnyAsync(x => x.AssetId == asset.Id()));
        Assert.False(await db.Employees.AnyAsync(e => e.ObjectGuid == employee));
        Assert.Equal([AuditAction.Created], await AuditActionsAsync(db, asset.Id()));
    }

    [SqlServerFact]
    public async Task A_return_whose_audit_record_fails_leaves_the_asset_with_its_holder()
    {
        using var owner = _api!.CreateSignedInClient(User);
        var assigned = await owner.AssignAsync(await owner.CreateAssetAsync(_refs.NewAssetBody()), await owner.EmployeeGuidAsync(_people.First));
        using var client = _api.CreateSignedInClient(AuditFailsUser);

        using var response = await client.PostReturnAsync(assigned.Id(), assigned.GetProperty("rowVersion").GetString());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var current = await owner.GetOkAsync($"/api/assets/{assigned.Id()}");
        Assert.Equal("Assigned", current.GetProperty("status").GetString());
        Assert.Equal(_people.First, current.GetProperty("activeAssignment").GetProperty("userName").GetString());
        await using var db = fixture.CreateContextFor(_connectionString);
        Assert.Null((await db.AssetAssignments.SingleAsync(x => x.AssetId == assigned.Id())).ReturnedAt);
    }

    [SqlServerFact]
    public async Task A_move_whose_audit_record_fails_leaves_the_asset_where_it_was()
    {
        using var owner = _api!.CreateSignedInClient(User);
        var asset = await owner.CreateAssetAsync(_refs.NewAssetBody());
        using var client = _api.CreateSignedInClient(AuditFailsUser);

        using var response = await client.PutLocationAsync(asset.Id(), new Dictionary<string, object?>
        {
            ["cityId"] = _refs.SecondCityId,
            ["departmentId"] = _refs.SecondDepartmentId,
            ["locationId"] = null,
            ["rowVersion"] = asset.GetProperty("rowVersion").GetString(),
        });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var current = await owner.GetOkAsync($"/api/assets/{asset.Id()}");
        Assert.Equal(_refs.CityId, current.GetProperty("city").GetProperty("id").GetInt32());
        Assert.Equal(asset.GetProperty("rowVersion").GetString(), current.GetProperty("rowVersion").GetString());
        await using var db = fixture.CreateContextFor(_connectionString);
        Assert.Equal([AuditAction.Created], await AuditActionsAsync(db, asset.Id()));
    }

    private static async Task<List<AuditAction>> AuditActionsAsync(ApplicationDbContext db, int assetId)
    {
        var entityId = assetId.ToString(CultureInfo.InvariantCulture);
        return await db.AuditLogs.Where(l => l.EntityName == nameof(Asset) && l.EntityId == entityId).OrderBy(l => l.Id).Select(l => l.Action).ToListAsync();
    }
}

/// <summary>
/// Holds the API's saves that match a condition: <see cref="HoldUntil"/> releases them together once the given
/// number has arrived (or after 15 seconds, leaving <see cref="Released"/> false); <see cref="RunBefore"/> runs
/// an action before the first one. Saves that do not match pass straight through.
/// </summary>
internal sealed class SaveGate : SaveChangesInterceptor
{
    private readonly Lock _lock = new();
    private Func<DbContext, bool>? _matches;
    private TaskCompletionSource? _release;
    private Func<Task>? _before;
    private int _expected;
    private int _arrived;

    public bool Released { get; private set; }

    public void HoldUntil(int parties, Func<DbContext, bool> matches)
    {
        lock (_lock)
        {
            (_matches, _expected, _arrived, Released, _before) = (matches, parties, 0, false, null);
            _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void RunBefore(Func<DbContext, bool> matches, Func<Task> action)
    {
        lock (_lock)
        {
            (_matches, _before, _release) = (matches, action, null);
        }
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Task? wait = null;
        Func<Task>? before = null;
        lock (_lock)
        {
            if (_matches is { } matches && matches(eventData.Context!))
            {
                if (_before is not null)
                {
                    (before, _before, _matches) = (_before, null, null);
                }
                else if (_release is { } release)
                {
                    if (++_arrived == _expected)
                    {
                        Released = true;
                        _matches = null;
                        release.TrySetResult();
                    }

                    wait = release.Task;
                }
            }
        }

        if (before is not null)
        {
            await before();
        }

        if (wait is not null)
        {
            try
            {
                await wait.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            catch (TimeoutException)
            {
                // Released stays false; the test reports that the race never happened.
            }
        }

        return result;
    }
}
