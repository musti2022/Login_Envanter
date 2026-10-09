using EnterpriseInventory.Infrastructure.ActiveDirectory;

namespace EnterpriseInventory.UnitTests.ActiveDirectory;

public class GroupMembershipTests
{
    private const string DomainSid = "S-1-5-21-1004336348-1177238915-682003330";
    private const string AllowedSid = DomainSid + "-1105";
    private const string AllowedDn = "CN=Bim_Envanter,OU=Groups,DC=corp,DC=example,DC=com";

    [Fact]
    public void Direct_member_is_allowed()
    {
        var user = User(memberOf: [AllowedDn]);

        Assert.True(GroupMembership.IsDirectMember(user, AllowedSid, AllowedDn));
    }

    [Fact]
    public void Direct_membership_ignores_dn_case_and_spacing()
    {
        var user = User(memberOf: ["cn=bim_envanter, ou=groups, dc=corp, dc=example, dc=com"]);

        Assert.True(GroupMembership.IsDirectMember(user, AllowedSid, AllowedDn));
    }

    [Fact]
    public void Group_with_the_same_name_elsewhere_is_not_the_allowed_group()
    {
        var user = User(memberOf: ["CN=Bim_Envanter,OU=Sahte,DC=corp,DC=example,DC=com"]);

        Assert.False(GroupMembership.IsDirectMember(user, AllowedSid, AllowedDn));
    }

    [Fact]
    public void Membership_through_another_group_does_not_count_as_direct()
    {
        var user = User(memberOf: ["CN=Envanter_Ekibi,OU=Groups,DC=corp,DC=example,DC=com"]);

        Assert.False(GroupMembership.IsDirectMember(user, AllowedSid, AllowedDn));
    }

    [Fact]
    public void Primary_group_counts_as_direct_membership()
    {
        var user = User(memberOf: [], primaryGroupId: 1105);

        Assert.True(GroupMembership.IsDirectMember(user, AllowedSid, AllowedDn));
    }

    [Fact]
    public void Primary_group_rid_from_another_domain_does_not_count()
    {
        var user = User(memberOf: [], primaryGroupId: 1105, sid: "S-1-5-21-9-9-9-2001");

        Assert.False(GroupMembership.IsDirectMember(user, AllowedSid, AllowedDn));
    }

    [Fact]
    public void Without_the_group_dn_only_the_primary_group_can_match()
    {
        var user = User(memberOf: [AllowedDn]);

        Assert.False(GroupMembership.IsDirectMember(user, AllowedSid, allowedGroupDn: null));
    }

    [Fact]
    public void Nested_membership_is_decided_by_token_groups()
    {
        Assert.True(GroupMembership.IsNestedMember([DomainSid + "-513", AllowedSid], AllowedSid));
        Assert.True(GroupMembership.IsNestedMember([AllowedSid.ToLowerInvariant()], AllowedSid));
        Assert.False(GroupMembership.IsNestedMember([DomainSid + "-513", DomainSid + "-1104"], AllowedSid));
        Assert.False(GroupMembership.IsNestedMember([], AllowedSid));
    }

    private static DirectoryUserEntry User(string[] memberOf, int? primaryGroupId = 513, string sid = DomainSid + "-2001") => new(
        "CN=ayse,OU=Staff,DC=corp,DC=example,DC=com",
        Guid.NewGuid(),
        sid,
        "ayse",
        "Ayşe",
        0x200,
        memberOf,
        primaryGroupId);
}
