using System.Globalization;
using EnterpriseInventory.Application.Employees;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Novell.Directory.Ldap;

namespace EnterpriseInventory.Infrastructure.ActiveDirectory;

/// <summary>
/// Finds employees in Active Directory over LDAPS with the service account, under
/// <see cref="ActiveDirectoryOptions.EmployeeBaseDn"/>. Group membership is not looked at: anyone with an account
/// can hold an asset. Searches match the start of names (an indexed lookup in AD, unlike "contains"), never send
/// a wildcard the user typed, and have the same deadline as a sign-in.
/// </summary>
internal sealed partial class LdapEmployeeDirectory(
    ILdapConnectionFactory connectionFactory,
    IOptions<ActiveDirectoryOptions> options,
    ILogger<LdapEmployeeDirectory> logger) : IEmployeeDirectory
{
    private const int AccountDisabledFlag = 0x2;

    /// <summary>Only people: computer accounts are users too, but never <c>objectCategory=person</c>.</summary>
    private const string People = "(objectCategory=person)(objectClass=user)";

    /// <summary>userAccountControl has the ACCOUNTDISABLE bit clear (LDAP_MATCHING_RULE_BIT_AND).</summary>
    private const string Enabled = "(!(userAccountControl:1.2.840.113556.1.4.803:=2))";

    private static readonly string[] SearchedAttributes = ["sAMAccountName", "displayName", "givenName", "sn", "mail"];

    private static readonly string[] PersonAttributes =
        ["objectGUID", "sAMAccountName", "displayName", "mail", "department", "title", "userAccountControl"];

    public async Task<DirectoryPeopleResult> SearchAsync(string term, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(term);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var words = term.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return new DirectoryPeopleResult(DirectoryLookupStatus.Succeeded, [], false);
        }

        // Every word must start one of the names: "mehmet öz" finds Mehmet Öztürk.
        var filter = $"(&{People}{Enabled}{string.Concat(words.Select(StartsAnyName))})";
        var result = await RunAsync(
            async (connection, settings, token) =>
            {
                var found = await connection.SearchManyAsync(settings.EmployeeBaseDn, filter, PersonAttributes, limit, token).ConfigureAwait(false);
                var people = new List<DirectoryPerson>(found.Entries.Count);
                foreach (var entry in found.Entries)
                {
                    if (ReadPerson(entry) is { } person)
                    {
                        people.Add(person);
                    }
                }

                return new DirectoryPeopleResult(DirectoryLookupStatus.Succeeded, people, found.Truncated);
            },
            "employee search",
            cancellationToken).ConfigureAwait(false);
        return result ?? DirectoryPeopleResult.Unavailable;
    }

    public async Task<DirectoryPersonResult> FindAsync(Guid objectGuid, CancellationToken cancellationToken)
    {
        var filter = $"(&{People}(objectGUID={DirectoryValues.EscapeFilterBytes(objectGuid.ToByteArray())}))";
        var result = await RunAsync(
            async (connection, settings, token) =>
            {
                var found = await connection.SearchAsync(settings.EmployeeBaseDn, LdapConnection.ScopeSub, filter, PersonAttributes, token)
                    .ConfigureAwait(false);
                return found.Entries.Count switch
                {
                    0 => DirectoryPersonResult.NotFound,
                    1 => ReadPerson(found.Entries[0]) is { } person
                        ? DirectoryPersonResult.Found(person)
                        : throw new InvalidDataException($"The account {found.Entries[0].Dn} is missing objectGUID or sAMAccountName."),
                    _ => throw new InvalidDataException($"{found.Entries.Count} accounts have the objectGUID {objectGuid}."),
                };
            },
            "employee lookup",
            cancellationToken).ConfigureAwait(false);
        return result ?? DirectoryPersonResult.Unavailable;
    }

    /// <summary>
    /// Connects, binds as the service account and runs <paramref name="operation"/> within one deadline. Returns
    /// <c>null</c> (logged) when the directory cannot be used.
    /// </summary>
    private async Task<T?> RunAsync<T>(
        Func<IDirectoryConnection, ActiveDirectoryOptions, CancellationToken, Task<T>> operation, string what, CancellationToken cancellationToken)
        where T : class
    {
        var settings = options.Value;
        var limit = TimeSpan.FromSeconds(settings.ConnectTimeoutSeconds + settings.OperationTimeoutSeconds);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(limit);
        try
        {
            using var connection = await connectionFactory.ConnectAsync(deadline.Token).ConfigureAwait(false);
            try
            {
                await connection.BindAsync(settings.ServiceAccountBindName, settings.ServiceAccountPassword, deadline.Token).ConfigureAwait(false);
            }
            catch (LdapException ex) when (ex.ResultCode == LdapException.InvalidCredentials)
            {
                LogServiceAccountRefused(settings.ServiceAccountUserName, what);
                return null;
            }

            return await operation(connection, settings, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            LogDirectoryUnavailable(what, DirectoryFailure.Timeout, $"It did not finish within {limit.TotalSeconds:0} seconds.");
            return null;
        }
        catch (DirectoryUnavailableException ex)
        {
            LogDirectoryUnavailable(what, ex.Failure, ex.Message);
            return null;
        }
        catch (LdapException ex)
        {
            LogDirectoryError(what, ex.ResultCode, ex.LdapErrorMessage);
            return null;
        }
        catch (InvalidDataException ex)
        {
            LogUnexpectedData(what, ex.Message);
            return null;
        }
    }

    private static string StartsAnyName(string word)
    {
        var value = DirectoryValues.EscapeFilterValue(word);
        return $"(|{string.Concat(SearchedAttributes.Select(attribute => $"({attribute}={value}*)"))})";
    }

    /// <summary>The person in an entry, or <c>null</c> for an entry without the identifiers every person has.</summary>
    private static DirectoryPerson? ReadPerson(LdapEntry entry)
    {
        var attributes = entry.GetAttributeSet();
        var guid = attributes.Find("objectGUID")?.ByteValue;
        var sam = attributes.Find("sAMAccountName")?.StringValue;
        if (guid is not { Length: 16 } || string.IsNullOrEmpty(sam))
        {
            return null;
        }

        var displayName = Text(attributes, "displayName");
        var flags = attributes.Find("userAccountControl")?.StringValue;
        return new DirectoryPerson(
            new Guid(guid),
            sam,
            displayName ?? sam,
            Text(attributes, "mail"),
            Text(attributes, "department"),
            Text(attributes, "title"),
            // Without the flags the account's state is unknown; it is not treated as enabled.
            int.TryParse(flags, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && (value & AccountDisabledFlag) == 0);
    }

    private static string? Text(LdapAttributeSet attributes, string name) =>
        attributes.Find(name)?.StringValue is { } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    [LoggerMessage(Level = LogLevel.Error, Message = "The directory refused the service account {ServiceAccount}; {Operation} failed")]
    private partial void LogServiceAccountRefused(string serviceAccount, string operation);

    [LoggerMessage(Level = LogLevel.Error, Message = "Active Directory is unavailable for the {Operation} ({Failure}): {Reason}")]
    private partial void LogDirectoryUnavailable(string operation, DirectoryFailure failure, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Active Directory returned LDAP result {ResultCode} for the {Operation}: {ServerMessage}")]
    private partial void LogDirectoryError(string operation, int resultCode, string? serverMessage);

    [LoggerMessage(Level = LogLevel.Error, Message = "Active Directory returned unexpected data for the {Operation}: {Reason}")]
    private partial void LogUnexpectedData(string operation, string reason);
}
