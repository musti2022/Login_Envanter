using Novell.Directory.Ldap;

namespace EnterpriseInventory.Infrastructure.ActiveDirectory;

internal static class LdapAttributes
{
    /// <summary>
    /// The attribute with this name, or <see langword="null"/> when the entry does not have it: the directory leaves
    /// out empty attributes (a user in no groups has no memberOf), and <see cref="LdapAttributeSet.GetAttribute(string)"/>
    /// throws for them.
    /// </summary>
    public static LdapAttribute? Find(this LdapAttributeSet attributes, string name) =>
        attributes.TryGetValue(name, out var attribute) ? attribute : null;
}
