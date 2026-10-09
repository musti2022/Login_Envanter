using EnterpriseInventory.Application.Abstractions;

namespace EnterpriseInventory.Api.Http;

/// <summary>The current HTTP request's correlation ID (see <see cref="CorrelationIdMiddleware"/>) and client address.</summary>
internal sealed class HttpRequestContext(IHttpContextAccessor accessor) : IRequestContext
{
    public string CorrelationId =>
        accessor.HttpContext?.TraceIdentifier ?? throw new InvalidOperationException("There is no current HTTP request.");

    public string? ClientAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
