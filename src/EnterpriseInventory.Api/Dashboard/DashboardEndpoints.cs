using EnterpriseInventory.Application.Dashboard;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EnterpriseInventory.Api.Dashboard;

/// <summary><c>/api/dashboard</c>: figures for the dashboard. Signed-in Administrators only (the fallback policy).</summary>
internal static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/dashboard/statistics", StatisticsAsync);
        return endpoints;
    }

    private static async Task<Ok<DashboardStatistics>> StatisticsAsync(IDashboardStore dashboard, CancellationToken cancellationToken) =>
        TypedResults.Ok(await dashboard.GetStatisticsAsync(cancellationToken));
}
