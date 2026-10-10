using EnterpriseInventory.Application.Abstractions;
using EnterpriseInventory.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EnterpriseInventory.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Stamps CreatedAt/CreatedBy and UpdatedAt/UpdatedBy on save. Any write without a signed-in user is
/// refused, whether or not the changed records have audit fields, so no data can be changed anonymously.
/// </summary>
public sealed class AuditableEntityInterceptor(ICurrentUser currentUser, TimeProvider timeProvider) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var entries = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();
        if (entries.Count == 0)
        {
            return;
        }

        var userName = currentUser.UserName;
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new InvalidOperationException("Changes cannot be saved without a signed-in user.");
        }

        var now = timeProvider.GetUtcNow();
        foreach (var entry in entries)
        {
            if (entry.Entity is not AuditableEntity auditable)
            {
                continue;
            }

            if (entry.State == EntityState.Added)
            {
                auditable.MarkCreated(userName, now);
            }
            else if (entry.State == EntityState.Modified)
            {
                auditable.MarkUpdated(userName, now);
            }
        }
    }
}
