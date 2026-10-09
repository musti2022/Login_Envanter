using EnterpriseInventory.Application.Assets;
using EnterpriseInventory.Application.Authentication;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseInventory.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        services.AddScoped<SignInHandler>();
        services.AddScoped<AssetService>();
        return services;
    }
}
