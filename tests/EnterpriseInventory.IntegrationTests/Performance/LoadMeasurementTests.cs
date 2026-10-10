using System.Globalization;
using System.Net;
using System.Text;
using EnterpriseInventory.Infrastructure.Persistence;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Assets;
using EnterpriseInventory.IntegrationTests.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace EnterpriseInventory.IntegrationTests.Performance;

/// <summary>
/// Measures the screens' requests on a representative inventory (<see cref="LoadSeed"/>): SQL commands per request,
/// pages SQL Server read for them, its CPU time, and the request's elapsed time in the API. Opt-in, because the seed
/// takes minutes: <c>EI_PERF_REPORT=/path/report.md</c> (and <c>EI_TEST_SQL_CONNECTION</c>) runs it and writes the
/// results there; <c>EI_PERF_ASSETS</c> changes the number of assets. See docs/performance.md.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class LoadMeasurementTests(SqlServerDatabaseFixture fixture, ITestOutputHelper output)
{
    private const int WarmUps = 2;
    private const int Runs = 15;

    [PerformanceFact]
    public async Task Measure_the_screens_requests_on_a_representative_inventory()
    {
        var reportPath = PerformanceFactAttribute.ReportPath!;
        var assets = int.TryParse(Environment.GetEnvironmentVariable("EI_PERF_ASSETS"), CultureInfo.InvariantCulture, out var n) ? n : 20_000;

        var (connectionString, data) = await DatabaseAsync(assets);
        output.WriteLine($"Seeded {data.AssetCount} assets, {data.AssignmentCount} assignments, {data.AuditLogCount} audit records in {data.SeedTime}.");

        var employees = new FakeEmployees(1);
        var meter = new SqlMeter();
        var server = new ServerStatistics(connectionString);
        await using var api = new TestApiFactory(
            connectionString,
            settings: employees.Settings(new Dictionary<string, string?> { ["RateLimiting:PermitLimit"] = "100000" }),
            configureServices: services =>
            {
                services.AddSingleton<TimeProvider>(new TestClock(LoadSeed.Now));
                services.ConfigureDbContext<ApplicationDbContext>(options => options.AddInterceptors(meter));
            });
        using var client = api.CreateSignedInClient();

        var lastPage = (int)Math.Ceiling(data.AssetCount * 0.98 / 25);
        var search = Uri.EscapeDataString(data.SampleEmployeeName.Split(' ')[1]);
        Scenario[] reads =
        [
            new("Envanter listesi, ilk sayfa (25)", "/api/assets"),
            new("Envanter, 100 satırlık sayfa", "/api/assets?pageSize=100"),
            new("Envanter, son sayfa", $"/api/assets?page={lastPage}"),
            new("Envanter, demirbaş koduyla arama", $"/api/assets?search={data.SampleAssetCode}"),
            new("Envanter, çalışan adıyla arama", $"/api/assets?search={search}"),
            new("Envanter, en büyük şehir + zimmetli", $"/api/assets?cityId={data.BusiestCityId}&status=Assigned"),
            new("Envanter, zimmetli kişiye göre sıralı", "/api/assets?sortBy=assignedDisplayName"),
            new("Envanter, arşiv", "/api/assets?archived=true"),
            new("Demirbaş detayı", $"/api/assets/{data.SampleAssetId}"),
            new("Demirbaş geçmişi", $"/api/assets/{data.SampleAssetId}/history"),
            new("Demirbaşın zimmet geçmişi", $"/api/assets/{data.SampleAssetId}/assignments"),
            new("Gösterge paneli", "/api/dashboard/statistics"),
            new("Denetim kayıtları, ilk sayfa", "/api/audit-logs"),
            new("Denetim kayıtları, demirbaş koduyla", $"/api/audit-logs?assetCode={data.SampleAssetCode}"),
            new("Denetim kayıtları, bir ay", "/api/audit-logs?from=2026-09-01T00:00:00%2B03:00&to=2026-10-01T00:00:00%2B03:00"),
            new("Envanter özeti, şehir", "/api/reports/asset-summary"),
            new("Envanter özeti, lokasyon", "/api/reports/asset-summary?groupBy=location"),
            new("Zimmet hareketleri, bir ay", "/api/reports/assignments?from=2026-09-01&to=2026-09-30"),
            new("Zimmet hareketleri, tümü", "/api/reports/assignments"),
            new("Zimmet hareketleri, çalışan adıyla", $"/api/reports/assignments?search={search}"),
            new("Lokasyon listesi", "/api/locations"),
            new("Model listesi", "/api/models"),
            new("Excel: bütün envanter", "/api/assets/export", Runs: 3),
            new("Excel: bir ayın zimmet hareketleri", "/api/reports/assignments/export?from=2026-09-01&to=2026-09-30", Runs: 5),
        ];

        var results = new List<Result>();
        var sql = new StringBuilder();
        foreach (var scenario in reads)
        {
            results.Add(await MeasureAsync(scenario, sql, () => client.GetOkBytesAsync(scenario.Path)));
        }

        // Writes: giving an available asset to an employee and taking it back, in one transaction each.
        var employee = await client.EmployeeGuidAsync(employees.First);
        var free = await FreeAssetIdAsync(connectionString);
        var asset = await client.GetOkAsync($"/api/assets/{free}");
        string rowVersion = asset.GetProperty("rowVersion").GetString()!;
        async Task<long> Assign()
        {
            var body = new Dictionary<string, object?> { ["employeeObjectGuid"] = employee, ["assignmentDescription"] = "Ölçüm", ["rowVersion"] = rowVersion };
            using var response = await client.PostAssignmentAsync(free, body);
            var details = await AssetApi.ReadAsync(response, HttpStatusCode.Created);
            rowVersion = details.GetProperty("rowVersion").GetString()!;
            return 0;
        }

        async Task<long> Return()
        {
            using var response = await client.PostReturnAsync(free, rowVersion);
            var details = await AssetApi.ReadAsync(response, HttpStatusCode.OK);
            rowVersion = details.GetProperty("rowVersion").GetString()!;
            return 0;
        }

        results.Add(await MeasureWriteAsync(new Scenario("Zimmet ver (yazma, transaction)", $"POST /api/assets/{{id}}/assignments", Runs: 10), sql, Assign, Return));
        results.Add(await MeasureWriteAsync(new Scenario("İade al (yazma, transaction)", $"POST /api/assets/{{id}}/returns", Runs: 10), sql, Return, Assign, prepareFirst: true));

        await File.WriteAllTextAsync(reportPath, Report(data, results));
        await File.WriteAllTextAsync(Path.ChangeExtension(reportPath, ".sql.md"), sql.ToString());
        output.WriteLine(Report(data, results));

        async Task<Result> MeasureAsync(Scenario scenario, StringBuilder log, Func<Task<long>> call)
        {
            long bytes = 0;
            for (var i = 0; i < WarmUps; i++)
            {
                bytes = await call();
            }

            meter.Start();
            var before = await server.SnapshotAsync();
            await call();
            var cost = ServerStatistics.Difference(before, await server.SnapshotAsync());
            var commands = meter.Stop();
            var (median, p95) = await Timings.MeasureAsync(scenario.Runs, call);

            Log(log, scenario, commands, cost);
            return new Result(scenario, commands.Count, cost, median, p95, bytes);
        }

        async Task<Result> MeasureWriteAsync(Scenario scenario, StringBuilder log, Func<Task<long>> write, Func<Task<long>> undo, bool prepareFirst = false)
        {
            if (prepareFirst)
            {
                await undo();
            }

            meter.Start();
            var before = await server.SnapshotAsync();
            await write();
            var cost = ServerStatistics.Difference(before, await server.SnapshotAsync());
            var commands = meter.Stop();
            await undo();

            var elapsed = new List<double>();
            for (var i = 0; i < scenario.Runs; i++)
            {
                var (one, _) = await Timings.MeasureAsync(1, write);
                elapsed.Add(one);
                await undo();
            }

            if (prepareFirst)
            {
                await write();
            }

            elapsed.Sort();
            Log(log, scenario, commands, cost);
            return new Result(scenario, commands.Count, cost, elapsed[elapsed.Count / 2], elapsed[(int)Math.Ceiling(elapsed.Count * 0.95) - 1], 0);
        }
    }

    /// <summary>
    /// A new database of the fixture's (dropped afterwards), or with <c>EI_PERF_DATABASE</c> a database kept between
    /// runs: created, migrated and seeded on the first run, brought up to the latest migration on later ones, so
    /// a schema change can be measured on the same rows before and after.
    /// </summary>
    private async Task<(string ConnectionString, LoadData Data)> DatabaseAsync(int assets)
    {
        if (Environment.GetEnvironmentVariable("EI_PERF_DATABASE") is not { Length: > 0 } name)
        {
            var created = await fixture.CreateMigratedDatabaseAsync();
            return (created, await LoadSeed.SeedAsync(created, assets));
        }

        var server = new SqlConnectionStringBuilder(SqlServerDatabaseFixture.ServerConnectionString) { InitialCatalog = "master" };
        await using (var connection = new SqlConnection(server.ConnectionString))
        {
            await connection.OpenAsync();
#pragma warning disable CA2100 // The name is a parameter, quoted by QUOTENAME; the collation is a constant.
            await using var command = new SqlCommand(
                $"IF DB_ID(@name) IS NULL BEGIN DECLARE @create nvarchar(400) = N'CREATE DATABASE ' + QUOTENAME(@name) + N' COLLATE {SqlServerDatabaseFixture.Collation}'; EXEC sp_executesql @create; END", connection);
#pragma warning restore CA2100
            command.Parameters.AddWithValue("@name", name);
            await command.ExecuteNonQueryAsync();
        }

        var connectionString = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = name }.ConnectionString;
        await using (var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connectionString).Options))
        {
            await db.Database.MigrateAsync();
            if (await db.Assets.IgnoreQueryFilters().AnyAsync())
            {
                return (connectionString, await LoadSeed.DescribeAsync(connectionString, TimeSpan.Zero));
            }
        }

        return (connectionString, await LoadSeed.SeedAsync(connectionString, assets));
    }

    private static async Task<int> FreeAssetIdAsync(string connectionString)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connectionString).Options);
        return await db.Assets.Where(a => a.Status == Domain.Assets.AssetStatus.Available).OrderBy(a => a.Id).Select(a => a.Id).FirstAsync();
    }

    private static void Log(StringBuilder log, Scenario scenario, IReadOnlyList<string> commands, ServerCost cost)
    {
        log.AppendLine(CultureInfo.InvariantCulture, $"## {scenario.Name}").AppendLine().AppendLine(CultureInfo.InvariantCulture, $"`{scenario.Path}`").AppendLine();
        log.AppendLine("| Okunan sayfa | CPU (ms) | Çalışma | İfade |").AppendLine("| ---: | ---: | ---: | --- |");
        foreach (var statement in cost.Statements)
        {
            var text = string.Join(' ', statement.Text.Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Replace("|", "\\|", StringComparison.Ordinal);
            log.AppendLine(CultureInfo.InvariantCulture, $"| {statement.LogicalReads} | {statement.WorkerMicroseconds / 1000.0:N1} | {statement.Executions} | `{(text.Length > 300 ? text[..300] + "…" : text)}` |");
        }

        log.AppendLine();
        foreach (var command in commands)
        {
            log.AppendLine("```sql").AppendLine(command).AppendLine("```").AppendLine();
        }
    }

    private static string Report(LoadData data, List<Result> results)
    {
        static string Ms(double value) => value.ToString("N1", CultureInfo.GetCultureInfo("tr-TR"));
        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"Veri: {LoadSeed.Number(data.AssetCount)} demirbaş, {LoadSeed.Number(data.AssignmentCount)} zimmet dönemi, {LoadSeed.Number(data.AuditLogCount)} denetim kaydı, {LoadSeed.Number(data.EmployeeCount)} çalışan{(data.SeedTime > TimeSpan.Zero ? $" (yükleme {data.SeedTime.TotalSeconds:N0} sn)" : string.Empty)}.");
        report.AppendLine();
        report.AppendLine("| Senaryo | İstek | SQL komutu | Okunan sayfa | SQL CPU (ms) | Süre medyan / p95 (ms) | Yanıt (KB) |");
        report.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (var r in results)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"| {r.Scenario.Name} | `{r.Scenario.Path}` | {r.Commands} | {LoadSeed.Number(r.Cost.LogicalReads)} | {Ms(r.Cost.CpuMilliseconds)} | {Ms(r.Median)} / {Ms(r.P95)} | {(r.Bytes / 1024.0).ToString("N0", CultureInfo.GetCultureInfo("tr-TR"))} |");
        }

        return report.ToString();
    }

    private sealed record Scenario(string Name, string Path, int Runs = Runs);

    private sealed record Result(Scenario Scenario, int Commands, ServerCost Cost, double Median, double P95, long Bytes);
}

/// <summary>A fact that runs only when <c>EI_PERF_REPORT</c> names a file for the results and SQL Server is set.</summary>
public sealed class PerformanceFactAttribute : FactAttribute
{
    public static string? ReportPath => Environment.GetEnvironmentVariable("EI_PERF_REPORT") is { Length: > 0 } path ? path : null;

    public PerformanceFactAttribute()
    {
        if (ReportPath is null || SqlServerDatabaseFixture.ServerConnectionString is null)
        {
            Skip = "EI_PERF_REPORT and EI_TEST_SQL_CONNECTION are not both set; the load measurement is opt-in.";
        }
    }
}

internal static class ByteReads
{
    /// <summary>GETs <paramref name="pathAndQuery"/>, asserts <c>200</c> and returns the size of the body.</summary>
    public static async Task<long> GetOkBytesAsync(this HttpClient client, string pathAndQuery)
    {
        using var response = await client.GetAsync(AssetApi.Uri(pathAndQuery));
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{pathAndQuery}: {(int)response.StatusCode} {Encoding.UTF8.GetString(body)}");
        return body.Length;
    }
}
