using System.Collections.Concurrent;
using System.Data.Common;
using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Domain.Catalog;
using EnterpriseInventory.Domain.Employees;
using EnterpriseInventory.Domain.Organization;
using EnterpriseInventory.Infrastructure.Persistence;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseInventory.IntegrationTests.Assets;

/// <summary>
/// The SQL that <c>GET /api/assets</c> and <c>GET /api/assets/{id}</c> send, on a database of their own with 30
/// assets (half of them assigned): filtering, sorting and paging happen in SQL Server, and the number of commands
/// does not grow with the page size (no N+1 queries for brands, models, locations or holders).
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class AssetQueryTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime, IDisposable
{
    private const int AssetCount = 30;

    private readonly CommandLog _commands = new();
    private TestApiFactory? _api;
    private int _assignedAssetId;

    public async Task InitializeAsync()
    {
        if (SqlServerDatabaseFixture.ServerConnectionString is null)
        {
            return;
        }

        var connectionString = await fixture.CreateMigratedDatabaseAsync();
        var model = AssetModel.Create(Brand.Create("Dell"), "Latitude 5440");
        var city = City.Create("İstanbul");
        var location = Location.Create(city, "Merkez Ofis");
        var department = Department.Create("Bilgi İşlem");
        var assignedAt = fixture.Clock.GetUtcNow();
        var assets = Enumerable.Range(1, AssetCount).Select(i =>
        {
            var asset = Asset.Create($"Q-{i:000}", AssetType.Laptop, model, city, department, location, serialNumber: $"SNQ{i:000}");
            if (i % 2 == 0)
            {
                asset.Assign(PersistenceTestData.NewEmployee(assignedAt), $"Zimmet {i}", notes: null, "test.admin", assignedAt);
            }

            return asset;
        }).ToList();
        await using (var db = fixture.CreateContextFor(connectionString))
        {
            db.AddRange(assets);
            await db.SaveChangesAsync();
        }

        _assignedAssetId = assets[1].Id;
        _api = new TestApiFactory(
            connectionString,
            settings: new Dictionary<string, string?> { ["RateLimiting:PermitLimit"] = "1000" },
            configureServices: services => services.ConfigureDbContext<ApplicationDbContext>(options => options.AddInterceptors(_commands)));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _api?.Dispose();

    [SqlServerTheory]
    [InlineData(5)]
    [InlineData(25)]
    public async Task A_list_page_is_one_count_and_one_page_query_whatever_its_size(int pageSize)
    {
        using var client = _api!.CreateSignedInClient();
        _commands.Clear();

        var page = await client.GetOkAsync($"/api/assets?pageSize={pageSize}&sortBy=assignedDisplayName");

        Assert.Equal(pageSize, page.GetProperty("items").GetArrayLength());
        Assert.Equal(AssetCount, page.GetProperty("totalCount").GetInt32());
        Assert.Collection(
            _commands.Texts,
            count => Assert.Contains("COUNT(*)", count, StringComparison.Ordinal),
            rows =>
            {
                // Sorted and paged by SQL Server, with the asset code and ID as tie-breakers, so pages never overlap.
                Assert.Matches(@"\[a\]\.\[AssetCode\], \[a\]\.\[Id\]\s+OFFSET @\w+ ROWS FETCH NEXT @\w+ ROWS ONLY", rows);
            });
    }

    [SqlServerFact]
    public async Task Filters_and_search_are_sent_to_SQL_Server_as_parameters()
    {
        using var client = _api!.CreateSignedInClient();
        _commands.Clear();

        var page = await client.GetOkAsync("/api/assets?search=Q-01%27%3B--&status=Assigned&pageSize=50");

        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, _commands.Texts.Count);
        Assert.All(_commands.Texts, sql =>
        {
            Assert.Contains("WHERE", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("Q-01'", sql, StringComparison.Ordinal);
        });
    }

    [SqlServerFact]
    public async Task An_asset_with_its_lookups_and_holder_is_read_in_one_query()
    {
        using var client = _api!.CreateSignedInClient();
        _commands.Clear();

        var asset = await client.GetOkAsync($"/api/assets/{_assignedAssetId}");

        Assert.Equal("Zimmet 2", asset.GetProperty("activeAssignment").GetProperty("assignmentDescription").GetString());
        Assert.Single(_commands.Texts);
    }

    /// <summary>Records the text of every command the API's DbContext sends.</summary>
    private sealed class CommandLog : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> _texts = new();

        public IReadOnlyList<string> Texts => [.. _texts];

        public void Clear() => _texts.Clear();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            _texts.Enqueue(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            _texts.Enqueue(command.CommandText);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            _texts.Enqueue(command.CommandText);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            _texts.Enqueue(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }
    }
}
