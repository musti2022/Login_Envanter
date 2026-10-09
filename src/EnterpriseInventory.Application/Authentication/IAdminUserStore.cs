namespace EnterpriseInventory.Application.Authentication;

/// <summary>Keeps the application's record of administrators who signed in, separate from employees.</summary>
public interface IAdminUserStore
{
    /// <summary>
    /// Creates or refreshes the administrator's record and appends a sign-in audit entry, both in one transaction.
    /// </summary>
    Task RecordSignInAsync(DirectoryAccount account, CancellationToken cancellationToken);
}
