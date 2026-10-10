using EnterpriseInventory.Application.Assets;
using EnterpriseInventory.Application.Auditing;
using EnterpriseInventory.Application.Authentication;
using EnterpriseInventory.Application.Employees;
using EnterpriseInventory.Application.Lookups;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EnterpriseInventory.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        services.AddScoped<SignInHandler>();
        services.AddScoped<AssetService>();
        services.AddScoped<AssetExportService>();
        services.AddScoped<AssetChangePublisher>();
        services.TryAddSingleton<IAssetChangeNotifier, NoAssetChangeNotifier>();
        services.AddScoped<AssetAssignmentService>();
        services.AddScoped<LookupService>();
        services.AddScoped<EmployeeService>();
        services.AddScoped<AuditLogService>();
        return services;
    }
}
