namespace EnterpriseInventory.Infrastructure.ActiveDirectory;

/// <summary>A user account as read from the directory after the user proved their password.</summary>
internal sealed record DirectoryUserEntry(
    string DistinguishedName,
    Guid ObjectGuid,
    string SecurityIdentifier,
    string SamAccountName,
    string DisplayName,
    int UserAccountControl,
    IReadOnlyList<string> MemberOf,
    int? PrimaryGroupId);

/// <summary>
/// Decides membership of the allowed group, always by the group's SID from configuration, never by its name: a
/// group elsewhere with the same name grants nothing.
/// </summary>
internal static class GroupMembership
{
    /// <summary>
    /// <see cref="NestedGroupPolicy.DirectMembershipOnly"/>: the user is listed in the group itself (memberOf holds
    /// the DN of the group that carries the SID), or the group is the user's primary group (AD leaves the primary
    /// group out of memberOf). Membership through another group does not count.
    /// </summary>
    /// <param name="allowedGroupDn">DN of the group found by its SID; <c>null</c> when no such group exists.</param>
    public static bool IsDirectMember(DirectoryUserEntry user, string allowedGroupSid, string? allowedGroupDn)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (allowedGroupDn is not null && user.MemberOf.Any(dn => DistinguishedNames.AreEqual(dn, allowedGroupDn)))
        {
            return true;
        }

        var (groupDomain, groupRid) = DirectoryValues.SplitRid(allowedGroupSid);
        var (userDomain, _) = DirectoryValues.SplitRid(user.SecurityIdentifier);
        return user.PrimaryGroupId is { } primary
            && (uint)primary == groupRid
            && userDomain.Equals(groupDomain, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <see cref="NestedGroupPolicy.IncludeNested"/>: the SID is among the user's tokenGroups, which the domain
    /// controller computes from direct, nested (any depth) and primary group membership.
    /// </summary>
    public static bool IsNestedMember(IEnumerable<string> tokenGroups, string allowedGroupSid) =>
        tokenGroups.Contains(allowedGroupSid, StringComparer.OrdinalIgnoreCase);
}
