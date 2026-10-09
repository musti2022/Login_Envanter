namespace EnterpriseInventory.Application.Employees;

/// <summary>
/// People in the organisation's directory (Active Directory) who can be given assets. Read with the service
/// account; unlike sign-in, membership of the allowed group plays no part: any employee can hold an asset.
/// </summary>
public interface IEmployeeDirectory
{
    /// <summary>
    /// Enabled accounts for which every word of <paramref name="term"/> is the start of their logon name, display
    /// name, given name, surname or e-mail address; at most <paramref name="limit"/> of them.
    /// </summary>
    Task<DirectoryPeopleResult> SearchAsync(string term, int limit, CancellationToken cancellationToken);

    /// <summary>The person with this objectGUID, enabled or not, as the directory says now.</summary>
    Task<DirectoryPersonResult> FindAsync(Guid objectGuid, CancellationToken cancellationToken);
}

/// <summary>A directory account as an employee: who they are and whether the account can still be used.</summary>
/// <param name="ObjectGuid">AD objectGUID; stable across renames, the key employees are matched on.</param>
public sealed record DirectoryPerson(
    Guid ObjectGuid,
    string SamAccountName,
    string DisplayName,
    string? Email,
    string? Department,
    string? Title,
    bool IsEnabled);

public enum DirectoryLookupStatus
{
    Succeeded = 0,

    /// <summary>No such account under the search base (deleted, or moved outside it).</summary>
    NotFound = 1,

    /// <summary>The directory could not be reached or trusted.</summary>
    DirectoryUnavailable = 2,
}

/// <param name="HasMore">More people matched than were returned; the caller should type more of the name.</param>
public sealed record DirectoryPeopleResult(DirectoryLookupStatus Status, IReadOnlyList<DirectoryPerson> People, bool HasMore)
{
    public static DirectoryPeopleResult Unavailable { get; } = new(DirectoryLookupStatus.DirectoryUnavailable, [], false);
}

public sealed record DirectoryPersonResult(DirectoryLookupStatus Status, DirectoryPerson? Person)
{
    public static DirectoryPersonResult NotFound { get; } = new(DirectoryLookupStatus.NotFound, null);

    public static DirectoryPersonResult Unavailable { get; } = new(DirectoryLookupStatus.DirectoryUnavailable, null);

    public static DirectoryPersonResult Found(DirectoryPerson person) => new(DirectoryLookupStatus.Succeeded, person);
}
