using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EnterpriseInventory.Infrastructure.Persistence;

internal static class Transactions
{
    /// <summary>
    /// Runs <paramref name="operation"/> in a transaction of its own, inside the context's execution strategy: if
    /// a retrying strategy is ever enabled, a transient failure repeats the whole unit (reads, checks, writes and
    /// commit) rather than one statement of it. Each attempt starts with an empty change tracker, so it decides on
    /// what the database holds at that moment. The operation commits; returning without committing rolls back.
    /// </summary>
    public static Task<T> InTransactionAsync<T>(
        this ApplicationDbContext db, Func<IDbContextTransaction, CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(
            async token =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
                return await operation(transaction, token).ConfigureAwait(false);
            },
            cancellationToken);
}
