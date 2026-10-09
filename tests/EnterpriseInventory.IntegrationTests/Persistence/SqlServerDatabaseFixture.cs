using EnterpriseInventory.Infrastructure.Persistence;
using EnterpriseInventory.Infrastructure.Persistence.Interceptors;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseInventory.IntegrationTests.Persistence;

/// <summary>
/// Creates a uniquely named database on the SQL Server given in <c>EI_TEST_SQL_CONNECTION</c>, applies the
/// migrations to it and drops it when the tests finish. Without the variable the SQL Server tests are skipped.
/// </summary>
/// <remarks>
/// The account in the connection string needs CREATE DATABASE; use it only against a test server. The database
/// is created with the Turkish collation the application expects (see docs/database.md).
/// </remarks>
public sealed class SqlServerDatabaseFixture : IAsyncLifetime
{
    public const string ConnectionVariable = "EI_TEST_SQL_CONNECTION";
    public const string Collation = "Turkish_CI_AS";
    public const string DefaultUser = "test.admin";

    public static string? ServerConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionVariable) is { Length: > 0 } value ? value : null;

    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 10, 9, 6, 0, 0, TimeSpan.Zero));

    private readonly List<string> _createdDatabases = [];

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (ServerConnectionString is null)
        {
            return;
        }

        ConnectionString = await CreateEmptyDatabaseAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var connectionString in _createdDatabases)
        {
            await using var context = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connectionString).Options);
            await context.Database.EnsureDeletedAsync();
        }
    }

    /// <summary>Creates a database without any tables; it is dropped with the fixture.</summary>
    public async Task<string> CreateEmptyDatabaseAsync()
    {
        var databaseName = $"EI_Test_{Guid.NewGuid():N}";
        var master = new SqlConnectionStringBuilder(ServerConnectionString) { InitialCatalog = "master" };
        await using (var connection = new SqlConnection(master.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // The database name is generated above and the collation is a constant.
            command.CommandText = $"CREATE DATABASE [{databaseName}] COLLATE {Collation}";
#pragma warning restore CA2100
            await command.ExecuteNonQueryAsync();
        }

        var connectionString = new SqlConnectionStringBuilder(ServerConnectionString) { InitialCatalog = databaseName }.ConnectionString;
        _createdDatabases.Add(connectionString);
        return connectionString;
    }

    /// <summary>A new context whose saves are stamped with <paramref name="userName"/> and <see cref="Clock"/>.</summary>
    public ApplicationDbContext CreateContext(string? userName = DefaultUser)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .AddInterceptors(new AuditableEntityInterceptor(new TestCurrentUser(userName), Clock))
            .Options;
        return new ApplicationDbContext(options);
    }

    /// <summary>Saves <paramref name="entity"/> and everything reachable from it in its own context.</summary>
    public async Task<T> SaveAsync<T>(T entity, string? userName = DefaultUser)
        where T : class
    {
        await using var context = CreateContext(userName);
        context.Add(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    /// <summary>Saves several new entities (and everything reachable from them) in one save.</summary>
    public async Task SaveAllAsync(params object[] entities)
    {
        await using var context = CreateContext();
        context.AddRange(entities);
        await context.SaveChangesAsync();
    }
}

/// <summary>Tests sharing one migrated database. They run one after another and each creates its own rows.</summary>
[CollectionDefinition(Name)]
public sealed class SqlServerTestGroup : ICollectionFixture<SqlServerDatabaseFixture>
{
    public const string Name = "SQL Server";
}

/// <summary>A fact that runs only when <c>EI_TEST_SQL_CONNECTION</c> points at a SQL Server.</summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (SqlServerDatabaseFixture.ServerConnectionString is null)
        {
            Skip = $"{SqlServerDatabaseFixture.ConnectionVariable} is not set; SQL Server tests are skipped.";
        }
    }
}
