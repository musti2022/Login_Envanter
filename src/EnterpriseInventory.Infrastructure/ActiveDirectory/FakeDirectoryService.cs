using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EnterpriseInventory.Application.Authentication;
using EnterpriseInventory.Application.Employees;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EnterpriseInventory.Infrastructure.ActiveDirectory;

/// <summary>
/// Development-only stand-in for Active Directory while the real domain details are unknown. Its users come
/// from <see cref="ActiveDirectoryOptions.FakeUsers"/> in the developer's user-secrets. It refuses to run in any
/// environment other than Development, in addition to the startup check in <see cref="ActiveDirectoryOptionsValidator"/>.
/// Every fake user, group member or not, is also an employee who can be given assets.
/// </summary>
internal sealed class FakeDirectoryService : IDirectoryService, IEmployeeDirectory
{
    private readonly ActiveDirectoryOptions _options;

    public FakeDirectoryService(IOptions<ActiveDirectoryOptions> options, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("The fake directory can only be used in the Development environment.");
        }

        _options = options.Value;
    }

    public Task<DirectorySignInResult> SignInAsync(string userName, string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userName);
        ArgumentNullException.ThrowIfNull(password);

        var user = DirectoryValues.TryGetSamAccountName(userName, "fake.invalid", out var samAccountName)
            ? _options.FakeUsers.FirstOrDefault(u => u.UserName.Equals(samAccountName, StringComparison.OrdinalIgnoreCase))
            : null;

        // Compares fixed-length hashes so the time taken does not depend on how much of the password matched.
        var matches = user is not null && password.Length > 0
            && CryptographicOperations.FixedTimeEquals(Hash(password), Hash(user.Password));

        var result = !matches ? DirectorySignInResult.Failed(DirectorySignInStatus.InvalidCredentials)
            : user!.IsDisabled ? DirectorySignInResult.Failed(DirectorySignInStatus.AccountDisabled)
            : !user.IsAllowedGroupMember ? DirectorySignInResult.Failed(DirectorySignInStatus.NotAuthorized)
            : DirectorySignInResult.Succeeded(ToAccount(user));
        return Task.FromResult(result);
    }

    public Task<DirectoryAccessStatus> CheckAccessAsync(Guid objectGuid, CancellationToken cancellationToken)
    {
        var user = _options.FakeUsers.FirstOrDefault(u => ToAccount(u).ObjectGuid == objectGuid);
        var status = user is null ? DirectoryAccessStatus.AccountNotFound
            : user.IsDisabled ? DirectoryAccessStatus.AccountDisabled
            : !user.IsAllowedGroupMember ? DirectoryAccessStatus.NotAuthorized
            : DirectoryAccessStatus.Allowed;
        return Task.FromResult(status);
    }

    public Task<DirectoryPeopleResult> SearchAsync(string term, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(term);
        var words = term.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var matches = _options.FakeUsers
            .Where(user => !user.IsDisabled && words.Length > 0 && words.All(word => Names(user).Any(name => StartsWith(name, word))))
            .Select(ToPerson)
            .ToList();
        return Task.FromResult(new DirectoryPeopleResult(DirectoryLookupStatus.Succeeded, [.. matches.Take(limit)], matches.Count > limit));
    }

    public Task<DirectoryPersonResult> FindAsync(Guid objectGuid, CancellationToken cancellationToken)
    {
        var user = _options.FakeUsers.FirstOrDefault(u => ToAccount(u).ObjectGuid == objectGuid);
        return Task.FromResult(user is null ? DirectoryPersonResult.NotFound : DirectoryPersonResult.Found(ToPerson(user)));
    }

    /// <summary>What a search matches the start of, as in AD: logon name, display name and each of its words, e-mail.</summary>
    private static IEnumerable<string> Names(FakeDirectoryUser user) =>
        new[] { user.UserName, user.DisplayName, user.Email }
            .Concat(user.DisplayName?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [])
            .OfType<string>();

    // Ignores case and accents and treats i, ı, İ and I as one letter, like the inventory search.
    private static bool StartsWith(string name, string word) =>
        CultureInfo.InvariantCulture.CompareInfo.IsPrefix(Fold(name), Fold(word), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);

    private static string Fold(string text) => text.Replace('ı', 'i').Replace('İ', 'i');

    private static DirectoryPerson ToPerson(FakeDirectoryUser user)
    {
        var account = ToAccount(user);
        return new DirectoryPerson(
            account.ObjectGuid, account.SamAccountName, account.DisplayName, user.Email, user.Department, user.Title, !user.IsDisabled);
    }

    private static DirectoryAccount ToAccount(FakeDirectoryUser user)
    {
        // Stable identifiers derived from the name, so the same fake user maps to the same AdminUser record.
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes("fake-directory:" + user.UserName.ToUpperInvariant()));
        var rid = 10_000 + (BitConverter.ToUInt32(digest, 16) % 1_000_000);
        return new DirectoryAccount(
            new Guid(digest.AsSpan(0, 16)),
            $"S-1-5-21-0-0-0-{rid}",
            user.UserName,
            string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName);
    }

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
