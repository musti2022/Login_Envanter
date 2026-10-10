using EnterpriseInventory.Api.Http;
using EnterpriseInventory.Application.Employees;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EnterpriseInventory.Api.Employees;

/// <summary>
/// <c>/api/employees</c>: finding the people assets are given to. Signed-in Administrators only (the fallback
/// policy). The people found need no access to the application.
/// </summary>
internal static class EmployeeEndpoints
{
    public static IEndpointRouteBuilder MapEmployeeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/employees/search", SearchAsync);
        return endpoints;
    }

    private static async Task<Results<Ok<EmployeeSearchResponse>, ValidationProblem, ProblemHttpResult>> SearchAsync(
        [AsParameters] EmployeeSearchRequest request, EmployeeService employees, CancellationToken cancellationToken)
    {
        var result = await employees.SearchAsync(request, cancellationToken);
        return result switch
        {
            { Response: { } response } => TypedResults.Ok(response),
            { Errors: { } errors } => ApiResults.ValidationProblem(errors),
            _ => DirectoryUnavailable(),
        };
    }

    public static ProblemHttpResult DirectoryUnavailable() =>
        ApiResults.Problem(
            StatusCodes.Status503ServiceUnavailable,
            "Çalışan dizinine şu anda ulaşılamıyor.",
            "Active Directory yanıt vermiyor. Lütfen biraz sonra tekrar deneyin.",
            "directory_unavailable");
}
