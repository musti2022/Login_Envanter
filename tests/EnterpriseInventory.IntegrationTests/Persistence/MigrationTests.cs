using EnterpriseInventory.Application.Abstractions;
using EnterpriseInventory.Domain.Organization;
using EnterpriseInventory.Infrastructure;
using EnterpriseInventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
            "The model changed without a migration. Run: dotnet ef migrations add <Name> --project src/EnterpriseInventory.Infrastructure");
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
        services.AddSingleton<TimeProvider>(database.Clock);
        services.AddScoped<ICurrentUser>(_ => new TestCurrentUser("di.admin"));
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
