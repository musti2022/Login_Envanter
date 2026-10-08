using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseInventory.Infrastructure;

public static class DependencyInjection
{
    // Persistence (EF Core / SQL Server) and Active Directory (LDAPS) services are registered here
    // in later stages. Values come from configuration; no secrets are kept in the repository.
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return services;
    }
}
