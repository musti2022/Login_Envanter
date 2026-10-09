namespace EnterpriseInventory.Domain.Common;

/// <summary>
/// Entity with creation/update audit fields and an optimistic concurrency token.
/// The persistence layer stamps the audit fields when changes are saved.
/// </summary>
public abstract class AuditableEntity : Entity
{
    public const int UserNameMaxLength = 256;

    public DateTimeOffset CreatedAt { get; private set; }

    public string CreatedBy { get; private set; } = string.Empty;

    public DateTimeOffset? UpdatedAt { get; private set; }

    public string? UpdatedBy { get; private set; }

    /// <summary>SQL Server rowversion; a mismatch on save means someone else changed the record.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public void MarkCreated(string createdBy, DateTimeOffset createdAt)
    {
        CreatedBy = Guard.Required(createdBy, UserNameMaxLength, nameof(createdBy));
        CreatedAt = createdAt;
    }

    public void MarkUpdated(string updatedBy, DateTimeOffset updatedAt)
    {
        var user = Guard.Required(updatedBy, UserNameMaxLength, nameof(updatedBy));
        if (updatedAt < CreatedAt)
        {
            throw new DomainException(DomainErrors.Audit.UpdatedBeforeCreated, "The update time cannot be earlier than the creation time.");
        }

        UpdatedBy = user;
        UpdatedAt = updatedAt;
    }
}
