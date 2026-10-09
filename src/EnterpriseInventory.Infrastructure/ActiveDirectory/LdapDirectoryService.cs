using EnterpriseInventory.Application.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Novell.Directory.Ldap;

namespace EnterpriseInventory.Infrastructure.ActiveDirectory;

/// <summary>
/// Signs users in against Active Directory over LDAPS: a simple bind as <c>user@Domain</c> proves the password,
/// then, still as that user, the account is read and its membership of the allowed group is checked by SID
/// (<see cref="GroupMembership"/>). Every failure, unexpected answer or timeout is a refusal.
/// </summary>
internal sealed partial class LdapDirectoryService(
    ILdapConnectionFactory connectionFactory,
    IOptions<ActiveDirectoryOptions> options,
    ILogger<LdapDirectoryService> logger) : IDirectoryService
{
    private const int AccountDisabledFlag = 0x2;

    private static readonly string[] UserAttributes =
        ["objectGUID", "objectSid", "sAMAccountName", "displayName", "userAccountControl", "memberOf", "primaryGroupID"];

    private static readonly string[] TokenGroupsAttribute = ["tokenGroups"];
    private static readonly string[] NoAttributes = ["1.1"];

    public async Task<DirectorySignInResult> SignInAsync(string userName, string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userName);
        var settings = options.Value;

        // An empty password would make the bind anonymous; never send it.
        if (string.IsNullOrEmpty(password)
            || !DirectoryValues.TryGetSamAccountName(userName, settings.Domain, out var samAccountName))
        {
            return DirectorySignInResult.Failed(DirectorySignInStatus.InvalidCredentials);
        }

        try
        {
            using var connection = await connectionFactory.ConnectAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await connection.BindAsync($"{samAccountName}@{settings.Domain}", password, cancellationToken).ConfigureAwait(false);
            }
            catch (LdapException ex) when (ex.ResultCode == LdapException.InvalidCredentials)
            {
                return DirectorySignInResult.Failed(BindFailures.FromDiagnosticMessage(ex.LdapErrorMessage));
            }

            var user = await FindUserAsync(connection, settings, samAccountName, cancellationToken).ConfigureAwait(false);
            if (user is null)
            {
                LogOutsideBaseDn(samAccountName, settings.BaseDn);
                return DirectorySignInResult.Failed(DirectorySignInStatus.NotAuthorized);
            }

            if ((user.UserAccountControl & AccountDisabledFlag) != 0)
            {
                return DirectorySignInResult.Failed(DirectorySignInStatus.AccountDisabled);
            }

            if (!await IsAllowedAsync(connection, settings, user, cancellationToken).ConfigureAwait(false))
            {
                return DirectorySignInResult.Failed(DirectorySignInStatus.NotAuthorized);
            }

            return DirectorySignInResult.Succeeded(
                new DirectoryAccount(user.ObjectGuid, user.SecurityIdentifier, user.SamAccountName, user.DisplayName));
        }
        catch (DirectoryUnavailableException ex)
        {
            LogDirectoryUnavailable(ex.Failure, ex.Message);
            return DirectorySignInResult.Failed(DirectorySignInStatus.DirectoryUnavailable);
        }
        catch (LdapException ex)
        {
            LogDirectoryError(ex.ResultCode, ex.LdapErrorMessage);
            return DirectorySignInResult.Failed(DirectorySignInStatus.DirectoryUnavailable);
        }
        catch (InvalidDataException ex)
        {
            LogUnexpectedData(ex.Message);
            return DirectorySignInResult.Failed(DirectorySignInStatus.DirectoryUnavailable);
        }
    }

    private async Task<bool> IsAllowedAsync(
        ILdapConnection connection, ActiveDirectoryOptions settings, DirectoryUserEntry user, CancellationToken cancellationToken)
    {
        switch (settings.NestedGroupPolicy)
        {
            case NestedGroupPolicy.IncludeNested:
                var tokenGroups = await ReadTokenGroupsAsync(connection, user.DistinguishedName, cancellationToken).ConfigureAwait(false);
                return GroupMembership.IsNestedMember(tokenGroups, settings.AllowedGroupSid);

            case NestedGroupPolicy.DirectMembershipOnly:
                var groupDn = await FindGroupDnAsync(connection, settings, cancellationToken).ConfigureAwait(false);
                if (groupDn is null)
                {
                    LogAllowedGroupNotFound(settings.AllowedGroupSid);
                }

                return GroupMembership.IsDirectMember(user, settings.AllowedGroupSid, groupDn);

            default:
                return false;
        }
    }

    private async Task<DirectoryUserEntry?> FindUserAsync(
        ILdapConnection connection, ActiveDirectoryOptions settings, string samAccountName, CancellationToken cancellationToken)
    {
        var filter = $"(&(objectCategory=person)(objectClass=user)(sAMAccountName={DirectoryValues.EscapeFilterValue(samAccountName)}))";
        var entries = await SearchAsync(connection, settings.BaseDn, filter, UserAttributes, cancellationToken).ConfigureAwait(false);
        return entries.Count switch
        {
            0 => null,
            1 => ReadUser(entries[0]),
            _ => throw new InvalidDataException($"{entries.Count} accounts have the logon name {samAccountName}."),
        };
    }

    private async Task<string?> FindGroupDnAsync(ILdapConnection connection, ActiveDirectoryOptions settings, CancellationToken cancellationToken)
    {
        // The group may live anywhere in the domain, not only under BaseDn.
        var filter = $"(objectSid={DirectoryValues.EscapeFilterBytes(DirectoryValues.SidToBytes(settings.AllowedGroupSid))})";
        var domainDn = DistinguishedNames.FromDnsDomain(settings.Domain);
        var entries = await SearchAsync(connection, domainDn, filter, NoAttributes, cancellationToken).ConfigureAwait(false);
        return entries.Count == 1 ? entries[0].Dn : null;
    }

    private async Task<IReadOnlyList<string>> ReadTokenGroupsAsync(ILdapConnection connection, string userDn, CancellationToken cancellationToken)
    {
        // tokenGroups is computed by the domain controller and can only be read with a base-scope search.
        var entries = await SearchAsync(connection, userDn, "(objectClass=*)", TokenGroupsAttribute, cancellationToken, LdapConnection.ScopeBase)
            .ConfigureAwait(false);
        var values = entries.Count == 1 ? entries[0].GetAttributeSet().Find("tokenGroups")?.ByteValueArray : null;
        return values?.Select(sid => DirectoryValues.SidToString(sid)).ToList()
            ?? throw new InvalidDataException("The directory did not return the user's tokenGroups.");
    }

    private async Task<List<LdapEntry>> SearchAsync(
        ILdapConnection connection,
        string searchBase,
        string filter,
        string[] attributes,
        CancellationToken cancellationToken,
        int scope = LdapConnection.ScopeSub)
    {
        var timeout = options.Value.OperationTimeoutSeconds;
        var constraints = new LdapSearchConstraints
        {
            MaxResults = 2,
            ServerTimeLimit = timeout,
            TimeLimit = (int)TimeSpan.FromSeconds(timeout).TotalMilliseconds,
            ReferralFollowing = false,
        };

        var results = await connection.SearchAsync(searchBase, scope, filter, attributes, false, constraints, cancellationToken)
            .ConfigureAwait(false);
        var entries = new List<LdapEntry>();
        while (await results.HasMoreAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                entries.Add(await results.NextAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (LdapReferralException)
            {
                // AD refers subtree searches at the domain root to its DNS partitions; they hold no users or groups.
            }
        }

        return entries;
    }

    private static DirectoryUserEntry ReadUser(LdapEntry entry)
    {
        var attributes = entry.GetAttributeSet();
        var guid = attributes.Find("objectGUID")?.ByteValue;
        var sid = attributes.Find("objectSid")?.ByteValue;
        var sam = attributes.Find("sAMAccountName")?.StringValue;
        if (guid is not { Length: 16 } || sid is null || string.IsNullOrEmpty(sam))
        {
            throw new InvalidDataException($"The account {entry.Dn} is missing objectGUID, objectSid or sAMAccountName.");
        }

        var displayName = attributes.Find("displayName")?.StringValue;
        return new DirectoryUserEntry(
            entry.Dn,
            new Guid(guid),
            DirectoryValues.SidToString(sid),
            sam,
            string.IsNullOrWhiteSpace(displayName) ? sam : displayName.Trim(),
            ReadInt(attributes, "userAccountControl") ?? 0,
            attributes.Find("memberOf")?.StringValueArray ?? [],
            ReadInt(attributes, "primaryGroupID"));
    }

    private static int? ReadInt(LdapAttributeSet attributes, string name) =>
        int.TryParse(attributes.Find(name)?.StringValue, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "{UserName} proved their password but is not under BaseDn {BaseDn}; access refused")]
    private partial void LogOutsideBaseDn(string userName, string baseDn);

    [LoggerMessage(Level = LogLevel.Error, Message = "No group with the configured AllowedGroupSid {GroupSid} was found; direct membership cannot be confirmed")]
    private partial void LogAllowedGroupNotFound(string groupSid);

    [LoggerMessage(Level = LogLevel.Error, Message = "Active Directory is unavailable ({Failure}): {Reason}")]
    private partial void LogDirectoryUnavailable(DirectoryFailure failure, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Active Directory returned LDAP result {ResultCode}: {ServerMessage}")]
    private partial void LogDirectoryError(int resultCode, string? serverMessage);

    [LoggerMessage(Level = LogLevel.Error, Message = "Active Directory returned unexpected data: {Reason}")]
    private partial void LogUnexpectedData(string reason);
}
