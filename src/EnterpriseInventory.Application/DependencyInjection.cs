using EnterpriseInventory.Application.Assets;
using EnterpriseInventory.Application.Authentication;
using EnterpriseInventory.Application.Employees;
using EnterpriseInventory.Application.Lookups;
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
        services.AddScoped<LookupService>();
        services.AddScoped<EmployeeService>();
        return services;
    }
}
