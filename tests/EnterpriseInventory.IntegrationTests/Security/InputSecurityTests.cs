using System.Net;
using System.Text.Json;
using EnterpriseInventory.Infrastructure.Persistence;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Assets;
using EnterpriseInventory.IntegrationTests.Performance;
using EnterpriseInventory.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace EnterpriseInventory.IntegrationTests.Security;

/// <summary>
/// Hostile input on SQL Server: SQL and LIKE metacharacters in every free-text filter, and fields a request has no
/// business setting. Inputs are parameters, never SQL text; unknown JSON fields are ignored.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class InputSecurityTests(SqlServerDatabaseFixture fixture, ITestOutputHelper output) : IAsyncLifetime, IDisposable
{
    private const string User = "ayse.admin";

    /// <summary>None of these is in the seeded data: each holds a quote, a semicolon, a LIKE wildcard or the escape character.</summary>
    public static TheoryData<string> Probes => new()
    {
        "' OR '1'='1",
        "'; DROP TABLE Assets; --",
        "x'; UPDATE Assets SET IsDeleted = 1; --",
        "1; WAITFOR DELAY '0:0:5' --",
        "' UNION SELECT name FROM sys.tables --",
        "%' OR 1=1 --",
        "%",
        "_",
        "[a-z]%",
        "]",
        "\\",
        "\\%",
        "--",
        "/* */",
    };

    /// <summary>Every free-text filter of the API, with <c>{0}</c> for the probe.</summary>
    private static readonly string[] Filters =
    [
        "/api/assets?search={0}",
        "/api/assets?archived=true&search={0}",
        "/api/assets/export?search={0}",
        "/api/reports/asset-summary?search={0}",
        "/api/reports/asset-summary/export?search={0}",
        "/api/reports/assignments?search={0}",
        "/api/reports/assignments/export?search={0}",
        "/api/audit-logs?assetCode={0}",
        "/api/audit-logs?userName={0}",
        "/api/audit-logs?correlationId={0}",
        "/api/audit-logs?entityName=Asset&entityId={0}",
        "/api/employees/search?q={0}",
    ];

    private readonly SqlMeter _meter = new();
    private string _connectionString = string.Empty;
    private InventoryReferences _refs = null!;
    private TestApiFactory? _api;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        if (SqlServerDatabaseFixture.ServerConnectionString is null)
        {
            return;
        }

        _connectionString = await fixture.SharedAsync("input-security-tests", async () =>
        {
            var database = await fixture.CreateMigratedDatabaseAsync();
            await LoadSeed.SeedAsync(database, assets: 150, employees: 40);
            return database;
        });
        _refs = await InventoryReferences.SeedAsync(fixture, _connectionString);
        _api = new TestApiFactory(
            _connectionString,
            settings: new Dictionary<string, string?>(LoginEndpointTests.FakeDirectory) { ["RateLimiting:PermitLimit"] = "10000" },
            configureServices: services => services.ConfigureDbContext<ApplicationDbContext>(options => options.AddInterceptors(_meter)));
        _client = _api.CreateSignedInClient(User);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _client?.Dispose();
        _api?.Dispose();
    }

    [SqlServerTheory]
    [MemberData(nameof(Probes))]
    public async Task Sql_and_like_metacharacters_match_nothing_and_change_nothing(string probe)
    {
        var before = await CountsAsync();
        var failures = new List<string>();

        foreach (var filter in Filters)
        {
            var path = string.Format(System.Globalization.CultureInfo.InvariantCulture, filter, Uri.EscapeDataString(probe));
            _meter.Start();
            using var response = await _client!.GetAsync(AssetApi.Uri(path));
            var commands = _meter.Stop();
            var body = await response.Content.ReadAsStringAsync();
            output.WriteLine($"{(int)response.StatusCode} {path}");

            // Refused as invalid, or answered with nothing found; never a server error.
            if (response.StatusCode == HttpStatusCode.OK)
            {
                if (response.Content.Headers.ContentType?.MediaType == "application/json" && Found(body) is { } found and > 0)
                {
                    failures.Add($"{path}: {found} found");
                }
            }
            else if (response.StatusCode != HttpStatusCode.BadRequest)
            {
                failures.Add($"{path}: {(int)response.StatusCode}");
            }

            if (body.Contains("SqlException", StringComparison.Ordinal) || body.Contains("Microsoft.Data", StringComparison.Ordinal))
            {
                failures.Add($"{path}: the answer names the database driver");
            }

            // The probe reaches SQL Server as a parameter value, never inside the command text.
            if (probe.Length > 2 && commands.Any(sql => sql.Contains(probe, StringComparison.Ordinal)))
            {
                failures.Add($"{path}: the input is in the SQL text");
            }
        }

        Assert.Empty(failures);
        Assert.Equal(before, await CountsAsync());
    }

    [SqlServerFact]
    public async Task The_filters_still_find_what_matches()
    {
        // The positive control for the probes: the same filters do search.
        Assert.Equal(10, (await _client!.GetOkAsync("/api/assets?search=DMR-00001")).GetProperty("totalCount").GetInt32());
        Assert.True((await _client!.GetOkAsync("/api/reports/assignments?search=DMR-00001")).GetProperty("totalCount").GetInt32() > 0);
        Assert.True((await _client!.GetOkAsync("/api/audit-logs?assetCode=DMR-00001")).GetProperty("totalCount").GetInt32() > 0);
        Assert.True((await _client!.GetOkAsync("/api/audit-logs?userName=admin.bir")).GetProperty("totalCount").GetInt32() > 0);
    }

    [SqlServerFact]
    public async Task A_new_asset_ignores_fields_the_server_sets()
    {
        var body = _refs.NewAssetBody();
        body["id"] = 999_999;
        body["isDeleted"] = true;
        body["isArchived"] = true;
        body["createdBy"] = "saldirgan";
        body["createdAt"] = "2000-01-01T00:00:00Z";
        body["updatedBy"] = "saldirgan";
        body["rowVersion"] = "AAAAAAAAAAE=";
        body["assignments"] = new[] { new { employeeId = 1, assignedAt = "2000-01-01T00:00:00Z" } };

        var created = await _client!.CreateAssetAsync(body);

        Assert.NotEqual(999_999, created.Id());
        Assert.False(created.GetProperty("isArchived").GetBoolean());
        Assert.Equal("Available", created.GetProperty("status").GetString());
        Assert.Equal(User, created.GetProperty("createdBy").GetString());
        Assert.True(created.GetProperty("createdAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-5));
        await using var db = Context();
        Assert.False(await db.AssetAssignments.IgnoreQueryFilters().AnyAsync(x => x.AssetId == created.Id()));
    }

    [SqlServerFact]
    public async Task An_update_cannot_archive_reassign_or_rewrite_who_created_the_asset()
    {
        var created = await _client!.CreateAssetAsync(_refs.NewAssetBody());
        var update = created.ToUpdateBody();
        update["description"] = "Güncellendi";
        update["isDeleted"] = true;
        update["isArchived"] = true;
        update["createdBy"] = "saldirgan";
        update["holderUserName"] = "calisan.0001";
        update["activeAssignment"] = new { employeeId = 1 };

        using var response = await _client!.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{created.Id()}", update);
        var updated = await AssetApi.ReadAsync(response, HttpStatusCode.OK);

        Assert.Equal("Güncellendi", updated.GetProperty("description").GetString());
        Assert.False(updated.GetProperty("isArchived").GetBoolean());
        Assert.Equal("Available", updated.GetProperty("status").GetString());
        Assert.Equal(User, updated.GetProperty("createdBy").GetString());
        await using var db = Context();
        Assert.False(await db.AssetAssignments.IgnoreQueryFilters().AnyAsync(x => x.AssetId == created.Id()));
    }

    [SqlServerFact]
    public async Task A_new_asset_cannot_start_assigned()
    {
        var body = _refs.NewAssetBody();
        body["status"] = "Assigned";

        using var response = await _client!.SendWithCsrfAsync(HttpMethod.Post, "/api/assets", body);

        var problem = await AssetApi.ReadAsync(response, HttpStatusCode.BadRequest);
        Assert.False(string.IsNullOrEmpty(problem.FieldError("status")));
    }

    /// <summary>The number of rows a page or report says it found; null for answers without one.</summary>
    private static int? Found(string body)
    {
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.GetArrayLength();
        }

        if (root.TryGetProperty("totalCount", out var total))
        {
            return total.GetInt32();
        }

        // People found in the directory.
        if (root.TryGetProperty("items", out var items))
        {
            return items.GetArrayLength();
        }

        // The summary: its total row.
        return root.TryGetProperty("total", out var summary) && summary.TryGetProperty("totalCount", out var count) ? count.GetInt32() : null;
    }

    private async Task<(int Assets, int Archived, int Assignments, int AuditLogs)> CountsAsync()
    {
        await using var db = Context();
        return (
            await db.Assets.IgnoreQueryFilters().CountAsync(),
            await db.Assets.IgnoreQueryFilters().CountAsync(a => a.IsDeleted),
            await db.AssetAssignments.IgnoreQueryFilters().CountAsync(),
            await db.AuditLogs.CountAsync());
    }

    private ApplicationDbContext Context() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(_connectionString).Options);
}
