using EnterpriseInventory.Application.Abstractions;

namespace EnterpriseInventory.Api.Http;

/// <summary>
/// The current HTTP request's correlation ID (see <see cref="CorrelationIdMiddleware"/>) and client address. Work the
/// API starts on its own, outside any request, uses the ID of its <see cref="BackgroundOperation"/> instead.
/// </summary>
internal sealed class HttpRequestContext(IHttpContextAccessor accessor, BackgroundOperation background) : IRequestContext
{
    public string CorrelationId => accessor.HttpContext?.TraceIdentifier ?? background.CorrelationId;

    public string? ClientAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}

/// <summary>
/// A unit of work the API does on its own rather than for a request, such as checking the sessions behind open live
/// connections. Each service scope is one operation with a correlation ID of its own for its logs and audit records.
/// </summary>
internal sealed class BackgroundOperation
{
    private string? _correlationId;

    public string CorrelationId => _correlationId ??= Guid.NewGuid().ToString("N");
}
