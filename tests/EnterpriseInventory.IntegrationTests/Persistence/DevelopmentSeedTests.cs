using EnterpriseInventory.Application.Abstractions;
using EnterpriseInventory.Infrastructure;
using EnterpriseInventory.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace EnterpriseInventory.IntegrationTests.Persistence;

[Collection(SqlServerTestGroup.Name)]
public class DevelopmentSeedTests(SqlServerDatabaseFixture database)
{
    [SqlServerFact]
    public async Task Running_the_seed_again_adds_nothing_and_it_adds_no_people()
    {
        var connectionString = await database.CreateMigratedDatabaseAsync();
        await using var services = Services(connectionString, Environments.Development);

        var first = await DevelopmentSeed.RunAsync(services, CancellationToken.None);
        var second = await DevelopmentSeed.RunAsync(services, CancellationToken.None);

        await using var context = database.CreateContextFor(connectionString);
        Assert.Equal(first, await context.Brands.CountAsync() + await context.AssetModels.CountAsync()
            + await context.Cities.CountAsync() + await context.Locations.CountAsync() + await context.Departments.CountAsync());
        Assert.True(first > 0);
        Assert.Equal(0, second);
        Assert.Equal([DevelopmentSeed.UserName], await context.Brands.Select(b => b.CreatedBy).Distinct().ToListAsync());
        Assert.Equal(0, await context.Employees.CountAsync());
        Assert.Equal(0, await context.AdminUsers.CountAsync());
    }

    [SqlServerFact]
    public async Task The_seed_adds_only_what_is_missing()
    {
        var connectionString = await database.CreateMigratedDatabaseAsync();
        await using (var context = database.CreateContextFor(connectionString))
        {
            // Already there under the Turkish collation ("İSTANBUL" is "İstanbul"), with one of its locations.
            var istanbul = Domain.Organization.City.Create("İSTANBUL");
            context.Locations.Add(Domain.Organization.Location.Create(istanbul, "Genel Müdürlük"));
            await context.SaveChangesAsync();
        }

        await using var services = Services(connectionString, Environments.Development);
        await DevelopmentSeed.RunAsync(services, CancellationToken.None);

        await using var check = database.CreateContextFor(connectionString);
        Assert.Equal(1, await check.Cities.CountAsync(c => c.Name == "İstanbul"));
        Assert.Equal(1, await check.Locations.CountAsync(l => l.Name == "Genel Müdürlük"));
        Assert.Equal(DevelopmentSeed.LocationsByCity.Sum(c => c.Value.Length), await check.Locations.CountAsync());
    }

    [SqlServerTheory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task The_seed_is_refused_outside_development(string environment)
    {
        var connectionString = await database.CreateMigratedDatabaseAsync();
        await using var services = Services(connectionString, environment);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DevelopmentSeed.RunAsync(services, CancellationToken.None));

        Assert.Contains("only be seeded in the Development environment", error.Message, StringComparison.Ordinal);
        await using var context = database.CreateContextFor(connectionString);
        Assert.Equal(0, await context.Brands.CountAsync());
    }

    private static ServiceProvider Services(string connectionString, string environment)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"ConnectionStrings:{DependencyInjection.ConnectionStringName}"] = connectionString })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = environment });
        services.AddScoped<ICurrentUser>(_ => new TestCurrentUser(null));
        services.AddScoped<IRequestContext>(_ => new TestRequestContext());
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
