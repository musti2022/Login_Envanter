using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.Text.Json;
using EnterpriseInventory.Infrastructure.Persistence;
using EnterpriseInventory.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseInventory.IntegrationTests.Api;

public class HealthEndpointTests
{
    [Fact]
    public async Task Liveness_is_anonymous_and_needs_no_database()
    {
        await using var api = new TestApiFactory();
        using var client = api.CreateAnonymousClient();

        using var response = await client.GetAsync(new Uri("/api/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(TestApiFactory.UnreachableDatabase)]
    [InlineData("")]
    public async Task Readiness_is_503_with_no_details_when_the_database_is_unavailable(string connectionString)
    {
        await using var api = new TestApiFactory(connectionString);
        using var client = api.CreateAnonymousClient();

        using var response = await client.GetAsync(new Uri("/api/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Readiness_answers_within_the_check_timeout_when_the_database_hangs()
    {
        // Accepts TCP connections but never answers, like a hung SQL Server.
        using var silentServer = new TcpListener(IPAddress.Loopback, 0);
        silentServer.Start();
        var port = ((IPEndPoint)silentServer.LocalEndpoint).Port;
        await using var api = new TestApiFactory($"Server=127.0.0.1,{port};Database=none;Connect Timeout=60;Encrypt=false");
        using var client = api.CreateAnonymousClient();
        var stopwatch = Stopwatch.StartNew();

        using var response = await client.GetAsync(new Uri("/api/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task Readiness_probes_share_one_database_check_for_a_few_seconds()
    {
        // A server that drops every connection: each database check costs exactly one connection attempt.
        // The anonymous probe must not open a connection per request, or a flood of probes during an outage
        // would tie up a thread each.
        using var droppingServer = new TcpListener(IPAddress.Loopback, 0);
        droppingServer.Start();
        var connections = 0;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                using var connection = await droppingServer.AcceptTcpClientAsync();
                Interlocked.Increment(ref connections);
            }
        });
        var port = ((IPEndPoint)droppingServer.LocalEndpoint).Port;
        await using var api = new TestApiFactory($"Server=127.0.0.1,{port};Database=none;Connect Timeout=5;Encrypt=false;Pooling=false");
        using var client = api.CreateAnonymousClient();
        var ready = new Uri("/api/health/ready", UriKind.Relative);

        var concurrent = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.GetAsync(ready)));
        using var afterwards = await client.GetAsync(ready);

        Assert.All(concurrent.Append(afterwards), response => Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode));
        Assert.Equal(1, Volatile.Read(ref connections));
        Array.ForEach(concurrent, response => response.Dispose());
    }

    [Fact]
    public async Task Details_require_an_administrator()
    {
        await using var api = new TestApiFactory();
        using var anonymous = api.CreateAnonymousClient();
        using var withoutRole = api.CreateSignedInClient(roles: "");

        using var anonymousResponse = await anonymous.GetAsync(new Uri("/api/health", UriKind.Relative));
        using var forbiddenResponse = await withoutRole.GetAsync(new Uri("/api/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal("Bu işlem için oturum açmanız gerekiyor.", (await ProblemJson.ReadAsync(anonymousResponse)).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
        Assert.Equal("Bu işlem için yetkiniz yok.", (await ProblemJson.ReadAsync(forbiddenResponse)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Details_name_the_failing_check_without_exception_messages()
    {
        await using var api = new TestApiFactory(connectionString: "");
        using var client = api.CreateSignedInClient();

        using var response = await client.GetAsync(new Uri("/api/health", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var database = HealthCheck(JsonDocument.Parse(body).RootElement, "database");
        Assert.Equal("Unhealthy", database.GetProperty("status").GetString());
        Assert.Equal("The database check failed.", database.GetProperty("description").GetString());
        Assert.DoesNotContain("DefaultConnection", body, StringComparison.Ordinal);
    }

    internal static JsonElement HealthCheck(JsonElement report, string name) =>
        report.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("name").GetString() == name);
}

[Collection(SqlServerTestGroup.Name)]
public class DatabaseHealthTests(SqlServerDatabaseFixture database)
{
    [SqlServerFact]
    public async Task Readiness_is_healthy_when_the_database_is_reachable_and_migrated()
    {
        await using var api = new TestApiFactory(database.ConnectionString);
        using var anonymous = api.CreateAnonymousClient();
        using var administrator = api.CreateSignedInClient();

        using var ready = await anonymous.GetAsync(new Uri("/api/health/ready", UriKind.Relative));
        var details = await administrator.GetFromJsonAsync<JsonElement>(new Uri("/api/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Healthy", await ready.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", HealthEndpointTests.HealthCheck(details, "database").GetProperty("status").GetString());
    }

    [SqlServerFact]
    public async Task Readiness_is_503_when_migrations_are_missing()
    {
        await using var api = new TestApiFactory(await database.CreateEmptyDatabaseAsync());
        using var anonymous = api.CreateAnonymousClient();
        using var administrator = api.CreateSignedInClient();

        using var ready = await anonymous.GetAsync(new Uri("/api/health/ready", UriKind.Relative));
        using var details = await administrator.GetAsync(new Uri("/api/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        var check = HealthEndpointTests.HealthCheck(await details.Content.ReadFromJsonAsync<JsonElement>(), "database");
        using var scope = api.Services.CreateScope();
        var migrations = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GetMigrations().Count();
        Assert.StartsWith($"{migrations} migration(s) not applied: ", check.GetProperty("description").GetString(), StringComparison.Ordinal);
    }
}
