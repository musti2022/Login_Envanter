using EnterpriseInventory.Application.Abstractions;
using EnterpriseInventory.Application.Assets;
using EnterpriseInventory.Application.Authentication;
using EnterpriseInventory.Infrastructure.ActiveDirectory;
using EnterpriseInventory.Infrastructure.Assets;
using EnterpriseInventory.Infrastructure.Identity;
using EnterpriseInventory.Infrastructure.Persistence;
using EnterpriseInventory.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EnterpriseInventory.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "DefaultConnection";

    // Values come from configuration; no secrets are kept in the repository.
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<SignInIdentity>();
        services.AddScoped(serviceProvider => new AuditableEntityInterceptor(
            new SignInAwareCurrentUser(serviceProvider.GetRequiredService<ICurrentUser>(), serviceProvider.GetRequiredService<SignInIdentity>()),
            serviceProvider.GetRequiredService<TimeProvider>()));
        services.AddScoped<IUserSessionService, UserSessionService>();
        services.AddScoped<IAssetStore, AssetStore>();
        services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
            }

            options.UseSqlServer(connectionString);
            options.AddInterceptors(serviceProvider.GetRequiredService<AuditableEntityInterceptor>());
        });

        // A singleton, so concurrent probes share one database check (see DatabaseHealthCheck).
        services.AddSingleton<DatabaseHealthCheck>();
        var healthChecks = services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: [HealthCheckTags.Ready], timeout: TimeSpan.FromSeconds(5));

        services.AddActiveDirectory(configuration, healthChecks);
        return services;
    }

    private static void AddActiveDirectory(this IServiceCollection services, IConfiguration configuration, IHealthChecksBuilder healthChecks)
    {
        services.AddOptions<ActiveDirectoryOptions>()
            .Bind(configuration.GetSection(ActiveDirectoryOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ActiveDirectoryOptions>, ActiveDirectoryOptionsValidator>();
        services.AddSingleton<ILdapConnectionFactory, LdapConnectionFactory>();
        services.AddScoped<LdapDirectoryService>();
        services.AddScoped<FakeDirectoryService>();

        // The mode is validated at startup: Fake can only be chosen in Development.
        services.AddScoped<IDirectoryService>(serviceProvider =>
            serviceProvider.GetRequiredService<IOptions<ActiveDirectoryOptions>>().Value.Mode == DirectoryMode.Fake
                ? serviceProvider.GetRequiredService<FakeDirectoryService>()
                : serviceProvider.GetRequiredService<LdapDirectoryService>());

        // A directory outage leaves existing sessions working, so it degrades the health report instead of
        // failing readiness.
        healthChecks.AddCheck<ActiveDirectoryHealthCheck>(
            "active-directory", failureStatus: HealthStatus.Degraded, timeout: TimeSpan.FromSeconds(30));
    }
}
