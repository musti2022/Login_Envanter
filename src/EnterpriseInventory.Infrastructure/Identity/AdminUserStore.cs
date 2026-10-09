using System.Globalization;
using System.Text.Json;
using EnterpriseInventory.Application.Abstractions;
using EnterpriseInventory.Application.Authentication;
using EnterpriseInventory.Domain.Auditing;
using EnterpriseInventory.Domain.Users;
using EnterpriseInventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseInventory.Infrastructure.Identity;

internal sealed class AdminUserStore(
    ApplicationDbContext db,
    SignInIdentity signInIdentity,
    IRequestContext requestContext,
    TimeProvider timeProvider) : IAdminUserStore
{
    public async Task RecordSignInAsync(DirectoryAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        using var actingUser = signInIdentity.ActAs(account.SamAccountName);
        var now = timeProvider.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var user = await db.AdminUsers.SingleOrDefaultAsync(u => u.ObjectGuid == account.ObjectGuid, cancellationToken)
            .ConfigureAwait(false);
        if (user is null)
        {
            user = AdminUser.Create(account.ObjectGuid, account.SamAccountName, account.DisplayName, now);
            db.AdminUsers.Add(user);
        }
        else
        {
            user.RecordLogin(account.SamAccountName, account.DisplayName, now);
        }

        // Saved first so the audit entry can name the record by its ID.
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var details = JsonSerializer.Serialize(new
        {
            user.SamAccountName,
            user.DisplayName,
            requestContext.ClientAddress,
        });
        db.AuditLogs.Add(AuditLog.Create(
            nameof(AdminUser),
            user.Id.ToString(CultureInfo.InvariantCulture),
            AuditAction.SignedIn,
            oldValues: null,
            newValues: details,
            account.SamAccountName,
            now,
            requestContext.CorrelationId));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
