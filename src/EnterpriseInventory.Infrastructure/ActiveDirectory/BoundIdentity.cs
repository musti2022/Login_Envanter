namespace EnterpriseInventory.Infrastructure.ActiveDirectory;

/// <summary>
/// The account a bind actually authenticated, from the directory's "Who am I?" answer (RFC 4532).
/// </summary>
/// <remarks>
/// A bind as <c>name@Domain</c> is resolved by AD against an explicit userPrincipalName before the implicit
/// <c>sAMAccountName@Domain</c>. If another account's userPrincipalName claims that name, its password opens the
/// bind, so the account found by sAMAccountName afterwards is not necessarily the one that proved its password.
/// Comparing the two closes that gap.
/// </remarks>
internal sealed record BoundIdentity(string? SamAccountName, string? DistinguishedName)
{
    /// <summary>
    /// Reads <c>u:DOMAIN\name</c> (what AD answers after a simple bind) or <c>dn:…</c>. A user principal name such
    /// as <c>u:name@domain</c> is not accepted: it is exactly the ambiguous form this check exists for.
    /// </summary>
    /// <exception cref="InvalidDataException">The answer is missing or in another form.</exception>
    public static BoundIdentity Parse(string? authorizationId)
    {
        if (authorizationId is not null && authorizationId.StartsWith("u:", StringComparison.OrdinalIgnoreCase))
        {
            var name = authorizationId[2..];
            var separator = name.IndexOf('\\', StringComparison.Ordinal);
            if (separator > 0 && separator < name.Length - 1 && name.IndexOf('\\', separator + 1) < 0)
            {
                return new BoundIdentity(name[(separator + 1)..], null);
            }
        }
        else if (authorizationId is not null && authorizationId.StartsWith("dn:", StringComparison.OrdinalIgnoreCase)
            && authorizationId.Length > 3)
        {
            return new BoundIdentity(null, authorizationId[3..]);
        }

        throw new InvalidDataException("The directory did not say which account the bind authenticated.");
    }

    /// <summary>Whether the identity can be told apart from the logon name before the account is looked up.</summary>
    public bool IsOtherThan(string samAccountName) =>
        SamAccountName is not null && !SamAccountName.Equals(samAccountName, StringComparison.OrdinalIgnoreCase);

    public bool IsOtherThan(DirectoryUserEntry user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return DistinguishedName is not null
            ? !DistinguishedNames.AreEqual(DistinguishedName, user.DistinguishedName)
            : IsOtherThan(user.SamAccountName);
    }

    public override string ToString() => SamAccountName ?? DistinguishedName ?? string.Empty;
}
