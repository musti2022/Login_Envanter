using EnterpriseInventory.Application.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Novell.Directory.Ldap;

namespace EnterpriseInventory.Infrastructure.ActiveDirectory;

/// <summary>
/// Signs users in against Active Directory over LDAPS: a simple bind as <c>user@Domain</c> proves the password and
/// "Who am I?" confirms which account it opened (<see cref="BoundIdentity"/>). Then, still as that user, the account
/// is read and its membership of the allowed group is checked by SID (<see cref="GroupMembership"/>). Every failure,
/// unexpected answer or timeout is a refusal, and the whole sign-in has one deadline.
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

        // One deadline for the whole sign-in, however many operations it takes.
        var limit = TimeSpan.FromSeconds(settings.ConnectTimeoutSeconds + settings.OperationTimeoutSeconds);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(limit);
        try
        {
            using var connection = await connectionFactory.ConnectAsync(deadline.Token).ConfigureAwait(false);
            try
            {
                await connection.BindAsync($"{samAccountName}@{settings.Domain}", password, deadline.Token).ConfigureAwait(false);
            }
            catch (LdapException ex) when (ex.ResultCode == LdapException.InvalidCredentials)
            {
                return DirectorySignInResult.Failed(BindFailures.FromDiagnosticMessage(ex.LdapErrorMessage));
            }

            var boundAs = BoundIdentity.Parse(await connection.WhoAmIAsync(deadline.Token).ConfigureAwait(false));
            if (boundAs.IsOtherThan(samAccountName))
            {
                LogBoundToAnotherAccount(samAccountName, boundAs.ToString());
                return DirectorySignInResult.Failed(DirectorySignInStatus.InvalidCredentials);
            }

            var (user, skippedReferrals) = await FindUserAsync(connection, settings, samAccountName, deadline.Token).ConfigureAwait(false);
            if (user is null)
            {
                LogOutsideBaseDn(samAccountName, settings.BaseDn, skippedReferrals);
                return DirectorySignInResult.Failed(DirectorySignInStatus.NotAuthorized);
            }

            if (boundAs.IsOtherThan(user))
            {
                LogBoundToAnotherAccount(samAccountName, boundAs.ToString());
                return DirectorySignInResult.Failed(DirectorySignInStatus.InvalidCredentials);
            }

            if ((user.UserAccountControl & AccountDisabledFlag) != 0)
            {
                return DirectorySignInResult.Failed(DirectorySignInStatus.AccountDisabled);
            }

            if (!await IsAllowedAsync(connection, settings, user, deadline.Token).ConfigureAwait(false))
            {
                return DirectorySignInResult.Failed(DirectorySignInStatus.NotAuthorized);
            }

            return DirectorySignInResult.Succeeded(
                new DirectoryAccount(user.ObjectGuid, user.SecurityIdentifier, user.SamAccountName, user.DisplayName));
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            LogDirectoryUnavailable(DirectoryFailure.Timeout, $"The sign-in did not finish within {limit.TotalSeconds:0} seconds.");
            return DirectorySignInResult.Failed(DirectorySignInStatus.DirectoryUnavailable);
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
        IDirectoryConnection connection, ActiveDirectoryOptions settings, DirectoryUserEntry user, CancellationToken cancellationToken)
    {
        switch (settings.NestedGroupPolicy)
        {
            case NestedGroupPolicy.IncludeNested:
                var tokenGroups = await ReadTokenGroupsAsync(connection, user.DistinguishedName, cancellationToken).ConfigureAwait(false);
                return GroupMembership.IsNestedMember(tokenGroups, settings.AllowedGroupSid);

            case NestedGroupPolicy.DirectMembershipOnly:
                var (groupDn, skippedReferrals) = await FindGroupDnAsync(connection, settings, cancellationToken).ConfigureAwait(false);
                if (groupDn is null)
                {
                    LogAllowedGroupNotFound(settings.AllowedGroupSid, skippedReferrals);
                }

                return GroupMembership.IsDirectMember(user, settings.AllowedGroupSid, groupDn);

            default:
                return false;
        }
    }

    private static async Task<(DirectoryUserEntry? User, int SkippedReferrals)> FindUserAsync(
        IDirectoryConnection connection, ActiveDirectoryOptions settings, string samAccountName, CancellationToken cancellationToken)
    {
        var filter = $"(&(objectCategory=person)(objectClass=user)(sAMAccountName={DirectoryValues.EscapeFilterValue(samAccountName)}))";
        var result = await connection.SearchAsync(settings.BaseDn, LdapConnection.ScopeSub, filter, UserAttributes, cancellationToken)
            .ConfigureAwait(false);
        return result.Entries.Count switch
        {
            0 => (null, result.SkippedReferrals),
            1 => (ReadUser(result.Entries[0]), result.SkippedReferrals),
            _ => throw new InvalidDataException($"{result.Entries.Count} accounts have the logon name {samAccountName}."),
        };
    }

    private static async Task<(string? Dn, int SkippedReferrals)> FindGroupDnAsync(
        IDirectoryConnection connection, ActiveDirectoryOptions settings, CancellationToken cancellationToken)
    {
        // The group may live anywhere in the domain, not only under BaseDn.
        var filter = $"(objectSid={DirectoryValues.EscapeFilterBytes(DirectoryValues.SidToBytes(settings.AllowedGroupSid))})";
        var domainDn = DistinguishedNames.FromDnsDomain(settings.Domain);
        var result = await connection.SearchAsync(domainDn, LdapConnection.ScopeSub, filter, NoAttributes, cancellationToken)
            .ConfigureAwait(false);
        return (result.Entries.Count == 1 ? result.Entries[0].Dn : null, result.SkippedReferrals);
    }

    private static async Task<IReadOnlyList<string>> ReadTokenGroupsAsync(
        IDirectoryConnection connection, string userDn, CancellationToken cancellationToken)
    {
        // tokenGroups is computed by the domain controller and can only be read with a base-scope search.
        var result = await connection.SearchAsync(userDn, LdapConnection.ScopeBase, "(objectClass=*)", TokenGroupsAttribute, cancellationToken)
            .ConfigureAwait(false);
        var values = result.Entries.Count == 1 ? result.Entries[0].GetAttributeSet().Find("tokenGroups")?.ByteValueArray : null;
        return values?.Select(sid => DirectoryValues.SidToString(sid)).ToList()
            ?? throw new InvalidDataException("The directory did not return the user's tokenGroups.");
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
            // Without it the account's state is unknown; reading it as "enabled" would fail open.
            ReadInt(attributes, "userAccountControl")
                ?? throw new InvalidDataException($"The account {entry.Dn} has no readable userAccountControl."),
            attributes.Find("memberOf")?.StringValueArray ?? [],
            ReadInt(attributes, "primaryGroupID"));
    }

    private static int? ReadInt(LdapAttributeSet attributes, string name) =>
        int.TryParse(attributes.Find(name)?.StringValue, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "{UserName} proved their password but is not under BaseDn {BaseDn} ({SkippedReferrals} referrals not followed); access refused")]
    private partial void LogOutsideBaseDn(string userName, string baseDn, int skippedReferrals);

    [LoggerMessage(Level = LogLevel.Error, Message = "No group with the configured AllowedGroupSid {GroupSid} was found ({SkippedReferrals} referrals not followed); direct membership cannot be confirmed")]
    private partial void LogAllowedGroupNotFound(string groupSid, int skippedReferrals);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The password typed for {UserName} opened the account {BoundAs} instead; refused. Another account's userPrincipalName probably claims this logon name")]
    private partial void LogBoundToAnotherAccount(string userName, string boundAs);

    [LoggerMessage(Level = LogLevel.Error, Message = "Active Directory is unavailable ({Failure}): {Reason}")]
    private partial void LogDirectoryUnavailable(DirectoryFailure failure, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Active Directory returned LDAP result {ResultCode}: {ServerMessage}")]
    private partial void LogDirectoryError(int resultCode, string? serverMessage);

    [LoggerMessage(Level = LogLevel.Error, Message = "Active Directory returned unexpected data: {Reason}")]
    private partial void LogUnexpectedData(string reason);
}
