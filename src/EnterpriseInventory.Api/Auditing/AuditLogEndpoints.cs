using EnterpriseInventory.Api.Http;
using EnterpriseInventory.Application.Assets;
using EnterpriseInventory.Application.Auditing;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EnterpriseInventory.Api.Auditing;

/// <summary>
/// <c>/api/audit-logs</c>: the audit log, read only. Signed-in Administrators only (the fallback policy); nothing
/// here changes or deletes a record.
/// </summary>
internal static class AuditLogEndpoints
{
    public const string Path = "/api/audit-logs";

    public static IEndpointRouteBuilder MapAuditLogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auditLogs = endpoints.MapGroup(Path);
        auditLogs.MapGet(string.Empty, ListAsync);
        auditLogs.MapGet("/{id:long}", GetAsync);
        return endpoints;
    }

    private static async Task<Results<Ok<PagedResult<AuditLogEntry>>, ValidationProblem>> ListAsync(
        [AsParameters] AuditLogRequest request, AuditLogService auditLogs, CancellationToken cancellationToken)
    {
        var result = await auditLogs.ListAsync(request, cancellationToken);
        return result.Page is { } page ? TypedResults.Ok(page) : ApiResults.ValidationProblem(result.Errors!);
    }

    private static async Task<Results<Ok<AuditLogEntry>, ProblemHttpResult>> GetAsync(long id, AuditLogService auditLogs, CancellationToken cancellationToken) =>
        await auditLogs.FindAsync(id, cancellationToken) is { } entry
            ? TypedResults.Ok(entry)
            : ApiResults.Problem(StatusCodes.Status404NotFound, "Denetim kaydı bulunamadı.", "Adres yanlış olabilir.", "audit_log_not_found");
}
