namespace EnterpriseInventory.Infrastructure.ActiveDirectory;

internal static class DistinguishedNames
{
    /// <summary>Whether <paramref name="dn"/> is <paramref name="container"/> or an object below it, ignoring case and spaces after commas.</summary>
    public static bool IsSameOrInside(string dn, string container)
    {
        var normalizedDn = Normalize(dn);
        var normalizedContainer = Normalize(container);
        return normalizedDn.Equals(normalizedContainer, StringComparison.OrdinalIgnoreCase)
            || normalizedDn.EndsWith("," + normalizedContainer, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether two DNs name the same object, ignoring case and spaces after commas.</summary>
    public static bool AreEqual(string first, string second) =>
        Normalize(first).Equals(Normalize(second), StringComparison.OrdinalIgnoreCase);

    /// <summary>The DN of a domain's naming context: <c>corp.example.com</c> becomes <c>DC=corp,DC=example,DC=com</c>.</summary>
    public static string FromDnsDomain(string domain) => string.Join(',', domain.Split('.').Select(label => "DC=" + label));

    private static string Normalize(string dn) => string.Join(',', dn.Split(',').Select(part => part.Trim()));
}
