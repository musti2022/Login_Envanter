namespace EnterpriseInventory.Application.Abstractions;

/// <summary>Facts about the request the current operation serves, for audit records and logs.</summary>
public interface IRequestContext
{
    /// <summary>The request's correlation ID, also returned to the client and written to every log line.</summary>
    string CorrelationId { get; }

    /// <summary>The client's IP address, when known.</summary>
    string? ClientAddress { get; }
}
