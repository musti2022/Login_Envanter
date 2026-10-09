using EnterpriseInventory.Application.Abstractions;
using EnterpriseInventory.Domain.Organization;
using EnterpriseInventory.Infrastructure;
using EnterpriseInventory.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace EnterpriseInventory.IntegrationTests.Persistence;

public class MigrationTests
{
    [Fact]
    public void Model_has_no_changes_missing_from_a_migration()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=unused;Database=unused")
            .Options;
        using var context = new ApplicationDbContext(options);

        Assert.False(
            context.Database.HasPendingModelChanges(),
            "The model changed without a migration. Run: dotnet ef migrations add <Name> --project src/EnterpriseInventory.Infrastructure --startup-project src/EnterpriseInventory.Infrastructure");
    }

    [Fact]
    public void The_committed_deployment_script_is_the_idempotent_script_of_every_migration()
    {
        using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer("Server=unused;Database=unused").Options);

        var generated = context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);

        Assert.True(
            Normalize(generated) == Normalize(File.ReadAllText(DeploymentScript.Path)),
            $"{DeploymentScript.RelativePath} is out of date. Run: {DeploymentScript.Command}");

        static string Normalize(string sql) => sql.TrimStart('\uFEFF').ReplaceLineEndings("\n").Trim();
    }
}

public class DbContextRegistrationTests
{
    [Fact]
    public async Task Each_scope_gets_its_own_context_and_failed_commands_are_not_retried()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{DependencyInjection.ConnectionStringName}"] = "Server=unused.invalid;Database=unused",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentUser>(_ => new TestCurrentUser("di.admin"));
        services.AddScoped<IRequestContext>(_ => new TestRequestContext());
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();
        var context = first.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Same(context, first.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        Assert.NotSame(context, second.ServiceProvider.GetRequiredService<ApplicationDbContext>());

        // If EnableRetryOnFailure is ever turned on, every explicit transaction (AssetStore.CreateAsync,
        // LookupStore, UserSessionService) has to run as a whole inside Database.CreateExecutionStrategy().ExecuteAsync.
        Assert.False(context.Database.CreateExecutionStrategy().RetriesOnFailure);
    }
}

[Collection(SqlServerTestGroup.Name)]
public class AppliedMigrationTests(SqlServerDatabaseFixture database)
{
    [SqlServerFact]
    public async Task All_migrations_are_applied_to_the_test_database()
    {
        await using var context = database.CreateContext();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync());
    }

    [SqlServerFact]
    public async Task AddInfrastructure_registers_a_context_that_stamps_the_signed_in_user()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{DependencyInjection.ConnectionStringName}"] = database.ConnectionString,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = Environments.Production });
        services.AddSingleton<TimeProvider>(database.Clock);
        services.AddScoped<ICurrentUser>(_ => new TestCurrentUser("di.admin"));
        services.AddScoped<IRequestContext>(_ => new TestRequestContext());
        services.AddInfrastructure(configuration);

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var city = City.Create(PersistenceTestData.Unique("Şehir"));
        context.Cities.Add(city);
        await context.SaveChangesAsync();

        Assert.Equal("di.admin", city.CreatedBy);
        Assert.Equal(database.Clock.GetUtcNow(), city.CreatedAt);
    }
}

/// <summary>
/// The deployment path for the schema: the committed idempotent script, applying it more than once, rolling back
/// migration by migration, and an API that never changes the schema itself.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public class DeploymentScriptTests(SqlServerDatabaseFixture database)
{
    [SqlServerFact]
    public async Task The_script_builds_the_schema_and_can_be_run_again()
    {
        var connectionString = await database.CreateEmptyDatabaseAsync();

        await RunScriptAsync(connectionString);
        await RunScriptAsync(connectionString);

        await using var context = database.CreateContextFor(connectionString);
        Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync());
        var indexes = await context.Database
            .SqlQuery<IndexRow>($"SELECT i.name AS Name, i.is_unique AS IsUnique, i.filter_definition AS FilterDefinition FROM sys.indexes i WHERE i.name IN ('IX_Assets_AssetCode', 'IX_Assets_SerialNumber', 'UX_AssetAssignments_AssetId_Active')")
            .ToListAsync();
        Assert.Equal(
            [
                new IndexRow("IX_Assets_AssetCode", true, null),
                new IndexRow("IX_Assets_SerialNumber", true, "([SerialNumber] IS NOT NULL)"),
                new IndexRow("UX_AssetAssignments_AssetId_Active", true, "([ReturnedAt] IS NULL)"),
            ],
            indexes.OrderBy(i => i.Name, StringComparer.Ordinal));
    }

    [SqlServerFact]
    public async Task Every_migration_can_be_rolled_back_and_applied_again_on_an_empty_database()
    {
        var connectionString = await database.CreateMigratedDatabaseAsync();
        await using var context = database.CreateContextFor(connectionString);
        var migrator = context.GetService<IMigrator>();
        var migrations = context.Database.GetMigrations().ToList();

        for (var i = migrations.Count - 2; i >= -1; i--)
        {
            var target = i >= 0 ? migrations[i] : Migration.InitialDatabase;
            await migrator.MigrateAsync(target);
            Assert.Equal(migrations.Take(i + 1), await context.Database.GetAppliedMigrationsAsync());
        }

        Assert.Equal(["__EFMigrationsHistory"], await context.Database.SqlQuery<string>($"SELECT name AS Value FROM sys.tables").ToListAsync());

        await migrator.MigrateAsync();
        Assert.Equal(migrations, await context.Database.GetAppliedMigrationsAsync());
    }

    /// <summary>
    /// The data-loss risk in deploy/database-rollback.md: rolling back below AddSignInAuditActions is refused once
    /// sign-ins are recorded, because the older check constraint does not allow those audit records (and audit
    /// records are never deleted). Each migration rolls back in its own transaction, so the steps above it stay
    /// rolled back.
    /// </summary>
    [SqlServerFact]
    public async Task Rolling_back_past_the_sign_in_audit_actions_is_refused_once_sign_ins_are_recorded()
    {
        var connectionString = await database.CreateMigratedDatabaseAsync();
        await using var context = database.CreateContextFor(connectionString);
        context.AuditLogs.Add(Domain.Auditing.AuditLog.Create(
            "AdminUser", "1", Domain.Auditing.AuditAction.SignedIn, null, "{}", "ayse.admin", database.Clock.GetUtcNow(), "corr"));
        await context.SaveChangesAsync();
        var migrator = context.GetService<IMigrator>();
        var migrations = context.Database.GetMigrations().ToList();

        await migrator.MigrateAsync(migrations[1]);
        await PersistenceTestData.AssertViolatesAsync("CK_AuditLogs_Action", () => migrator.MigrateAsync(migrations[0]));

        Assert.Equal(migrations.Take(2), await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1, await context.AuditLogs.CountAsync());
    }

    [SqlServerFact]
    public async Task The_API_never_creates_or_migrates_the_database_when_it_starts()
    {
        var connectionString = await database.CreateEmptyDatabaseAsync();
        using var api = new Api.TestApiFactory(
            connectionString, environment: "Production", settings: new Dictionary<string, string?> { ["AllowedHosts"] = "localhost" });
        using var client = api.CreateAnonymousClient();

        using var response = await client.GetAsync(new Uri("/api/health/ready", UriKind.Relative));

        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await using var context = database.CreateContextFor(connectionString);
        Assert.Empty(await context.Database.SqlQuery<string>($"SELECT name AS Value FROM sys.tables").ToListAsync());
    }

    /// <summary>Runs the committed script the way sqlcmd does: batch by batch, split on GO.</summary>
    private static async Task RunScriptAsync(string connectionString)
    {
        var batches = System.Text.RegularExpressions.Regex.Split(
            File.ReadAllText(DeploymentScript.Path), @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var batch in batches.Where(b => !string.IsNullOrWhiteSpace(b)))
        {
            await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // The committed deployment script, not user input.
            command.CommandText = batch;
#pragma warning restore CA2100
            await command.ExecuteNonQueryAsync();
        }
    }

    public sealed record IndexRow(string Name, bool IsUnique, string? FilterDefinition);
}

/// <summary>The idempotent migration script committed for deployments.</summary>
internal static class DeploymentScript
{
    public const string RelativePath = "deploy/sql/migrate-idempotent.sql";

    public const string Command =
        "dotnet ef migrations script --idempotent --project src/EnterpriseInventory.Infrastructure --startup-project src/EnterpriseInventory.Infrastructure -o deploy/sql/migrate-idempotent.sql";

    public static string Path => System.IO.Path.Combine(RepositoryRoot(), RelativePath);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "EnterpriseInventory.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("EnterpriseInventory.slnx was not found above the test output directory.");
    }
}
