using EnterpriseInventory.Domain.Common;

namespace EnterpriseInventory.Domain.Auditing;

/// <summary>
/// Append-only record of a change: which entity and record, what happened, the old and new values,
/// who did it, when, and the request's correlation ID. There are no update methods by design.
/// Old/new values are JSON snapshots that must never contain passwords, tokens or other secrets.
/// </summary>
public sealed class AuditLog
{
    public const int EntityNameMaxLength = 100;
    public const int EntityIdMaxLength = 64;
    public const int CorrelationIdMaxLength = 64;

    private AuditLog()
    {
    }

    public long Id { get; private set; }

    public string EntityName { get; private set; } = string.Empty;

    public string EntityId { get; private set; } = string.Empty;

    public AuditAction Action { get; private set; }

    public string? OldValues { get; private set; }

    public string? NewValues { get; private set; }

    public string UserName { get; private set; } = string.Empty;

    public DateTimeOffset Timestamp { get; private set; }

    public string CorrelationId { get; private set; } = string.Empty;

    public static AuditLog Create(
        string entityName,
        string entityId,
        AuditAction action,
        string? oldValues,
        string? newValues,
        string userName,
        DateTimeOffset timestamp,
        string correlationId)
    {
        var log = new AuditLog
        {
            EntityName = Guard.Required(entityName, EntityNameMaxLength, nameof(entityName)),
            EntityId = Guard.Required(entityId, EntityIdMaxLength, nameof(entityId)),
            Action = Guard.Defined(action, nameof(action)),
            OldValues = string.IsNullOrWhiteSpace(oldValues) ? null : oldValues,
            NewValues = string.IsNullOrWhiteSpace(newValues) ? null : newValues,
            UserName = Guard.Required(userName, AuditableEntity.UserNameMaxLength, nameof(userName)),
            Timestamp = timestamp,
            CorrelationId = Guard.Required(correlationId, CorrelationIdMaxLength, nameof(correlationId)),
        };

        if (log.OldValues is null && log.NewValues is null)
        {
            throw new DomainException(DomainErrors.Required, "An audit record needs old values, new values or both.");
        }

        return log;
    }
}
