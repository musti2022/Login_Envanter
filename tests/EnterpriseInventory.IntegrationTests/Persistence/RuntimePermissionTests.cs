using System.Net;
using System.Security.Cryptography;
using EnterpriseInventory.IntegrationTests.Api;
using EnterpriseInventory.IntegrationTests.Assets;
using Microsoft.Data.SqlClient;

namespace EnterpriseInventory.IntegrationTests.Persistence;

/// <summary>
/// Day 39: the runtime account is separate from the deployment account. The committed
/// <c>scripts/sql/grant-runtime-permissions.sql</c> gives it rows to read, add and change and nothing else;
/// <c>deploy/sql/verify-runtime-permissions.sql</c> proves it; and the application does all its work with just that.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class RuntimePermissionTests(SqlServerDatabaseFixture database) : IAsyncLifetime
{
    private const string GrantScript = "scripts/sql/grant-runtime-permissions.sql";
    private const string VerifyScript = "deploy/sql/verify-runtime-permissions.sql";

    private readonly List<string> _logins = [];

    private static string Master => new SqlConnectionStringBuilder(SqlServerDatabaseFixture.ServerConnectionString) { InitialCatalog = "master" }.ConnectionString;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        foreach (var login in _logins)
        {
            await ExecuteAsync(Master, $"DROP LOGIN [{login}]");
        }
    }

    [SqlServerFact]
    public async Task The_runtime_user_can_read_add_and_change_rows_and_nothing_more()
    {
        var connectionString = await database.CreateMigratedDatabaseAsync();
        var user = await RuntimeUserAsync(connectionString);

        var checks = await SqlcmdScript.RunAsync(connectionString, VerifyScript, new Dictionary<string, string> { ["RuntimeUser"] = user });

        Assert.All(checks, row => Assert.Equal("PASS", row[3]));
        Assert.Contains(checks, row => row[0] == "DATABASE CREATE TABLE" && row[2] == "False");
        Assert.Contains(checks, row => row[0] == "dbo.Assets UPDATE" && row[2] == "True");
        Assert.Contains(checks, row => row[0] == "dbo.Assets DELETE" && row[2] == "False");
        Assert.Contains(checks, row => row[0] == "dbo.AuditLogs INSERT" && row[2] == "True");
        Assert.Contains(checks, row => row[0] == "dbo.AuditLogs UPDATE" && row[2] == "False");
        Assert.Contains(checks, row => row[0] == "dbo.__EFMigrationsHistory SELECT" && row[2] == "True");
        Assert.Contains(checks, row => row[0] == "dbo.__EFMigrationsHistory INSERT" && row[2] == "False");

        // And in practice, not only on paper: every attempt is refused and nothing changes.
        string[] refused =
        [
            "CREATE TABLE dbo.RuntimeProbe (Id int)",
            "ALTER TABLE dbo.Assets ADD RuntimeProbe int NULL",
            "DROP TABLE dbo.AuditLogs",
            "TRUNCATE TABLE dbo.AuditLogs",
            "DELETE FROM dbo.Assets",
            "UPDATE dbo.AuditLogs SET CorrelationId = CorrelationId",
            "DELETE FROM dbo.AuditLogs",
            "INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion) VALUES (N'99999999999999_Probe', N'0')",
            "CREATE USER RuntimeProbe WITHOUT LOGIN",
            "GRANT DELETE ON SCHEMA::dbo TO ei_app_runtime",
        ];
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, "EXECUTE AS USER = @user", ("@user", user));
        foreach (var statement in refused)
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(connection, statement));
            Assert.True(error.Number is 229 or 262 or 1088 or 3701 or 4902 or 15151 or 15247 or 4613, $"{statement}: {error.Number} {error.Message}");
        }

        Assert.Equal(0, await ScalarAsync(connection, "SELECT COUNT(*) FROM dbo.AuditLogs"));
        await ExecuteAsync(connection, "REVERT");
        Assert.Equal(0, await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.tables WHERE name = N'RuntimeProbe'"));
        Assert.Equal(0, await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.columns WHERE name = N'RuntimeProbe'"));
    }

    [SqlServerFact]
    public async Task The_check_fails_when_the_runtime_user_has_been_given_more()
    {
        var connectionString = await database.CreateMigratedDatabaseAsync();
        var user = await RuntimeUserAsync(connectionString);
        await ExecuteAsync(connectionString, $"ALTER ROLE db_datawriter ADD MEMBER [{user}]");

        var error = await Assert.ThrowsAsync<SqlException>(
            () => SqlcmdScript.RunAsync(connectionString, VerifyScript, new Dictionary<string, string> { ["RuntimeUser"] = user }));

        Assert.Contains("beklenenden farklı", error.Message, StringComparison.Ordinal);
    }

    [SqlServerFact]
    public async Task The_application_does_all_its_work_with_the_runtime_rights_alone()
    {
        var connectionString = await database.CreateMigratedDatabaseAsync();
        var refs = await InventoryReferences.SeedAsync(database, connectionString);
        var runtime = await RuntimeLoginAsync(connectionString);
        var people = new FakeEmployees();
        const string Admin = "runtime.admin";
        const string Password = "fake-directory-password";
        var settings = people.Settings(new Dictionary<string, string?>
        {
            ["ActiveDirectory:FakeUsers:10:UserName"] = Admin,
            ["ActiveDirectory:FakeUsers:10:Password"] = Password,
            ["ActiveDirectory:FakeUsers:10:DisplayName"] = "Runtime Yönetici",
        });
        await using var api = new TestApiFactory(runtime, useTestAuthentication: false, settings: settings);
        using var client = api.CreateAnonymousClient();

        using (var ready = await client.GetAsync(new Uri("/api/health/ready", UriKind.Relative)))
        {
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        }

        await client.SignInAsync(Admin, Password);
        using (var brand = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/brands", new { name = PersistenceTestData.Unique("Marka") }))
        {
            Assert.Equal(HttpStatusCode.Created, brand.StatusCode);
        }

        var asset = await client.CreateAssetAsync(refs.NewAssetBody());
        var body = asset.ToUpdateBody();
        body["description"] = "Runtime hesabıyla güncellendi";
        using (var update = await client.SendWithCsrfAsync(HttpMethod.Put, $"/api/assets/{asset.Id()}", body))
        {
            asset = await AssetApi.ReadAsync(update, HttpStatusCode.OK);
        }

        asset = await client.AssignAsync(asset, await client.EmployeeGuidAsync(people.First));
        asset = await client.ReturnAsync(asset);
        using (var archive = await client.SendWithCsrfAsync(
            HttpMethod.Delete, $"/api/assets/{asset.Id()}?rowVersion={Uri.EscapeDataString(asset.GetProperty("rowVersion").GetString()!)}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        }

        var log = await client.GetOkAsync($"/api/audit-logs?entityName=Asset&entityId={asset.Id()}");
        Assert.Equal(5, log.GetProperty("totalCount").GetInt32());
        using (var report = await client.GetAsync(new Uri("/api/reports/asset-summary?groupBy=city", UriKind.Relative)))
        {
            Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        }

        using (var logout = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/logout"))
        {
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        }

        using var afterLogout = await client.GetAsync(AuthClient.Me);
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    /// <summary>A database user without a login, given the runtime rights by the committed script.</summary>
    private static async Task<string> RuntimeUserAsync(string connectionString)
    {
        var user = $"ei_runtime_{Guid.NewGuid():N}";
        await ExecuteAsync(connectionString, $"CREATE USER [{user}] WITHOUT LOGIN");
        await SqlcmdScript.RunAsync(connectionString, GrantScript, new Dictionary<string, string> { ["RuntimeUser"] = user });
        return user;
    }

    /// <summary>A SQL login of its own (the test server has no Windows accounts), given the runtime rights; dropped afterwards.</summary>
    private async Task<string> RuntimeLoginAsync(string connectionString)
    {
        var login = $"ei_runtime_{Guid.NewGuid():N}";
        var password = $"Rt1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}aZ";
        await ExecuteAsync(Master, $"CREATE LOGIN [{login}] WITH PASSWORD = N'{password}', CHECK_POLICY = ON");
        _logins.Add(login);
        await SqlcmdScript.RunAsync(connectionString, GrantScript, new Dictionary<string, string> { ["RuntimeUser"] = login });
        return new SqlConnectionStringBuilder(connectionString) { UserID = login, Password = password, IntegratedSecurity = false }.ConnectionString;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, sql);
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Test SQL; names are generated by the tests and values are parameters.
        command.CommandText = sql;
#pragma warning restore CA2100
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Constant test SQL.
        command.CommandText = sql;
#pragma warning restore CA2100
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
