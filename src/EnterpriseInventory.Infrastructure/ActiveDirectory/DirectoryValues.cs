using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EnterpriseInventory.Infrastructure.ActiveDirectory;

/// <summary>Conversions and checks for values exchanged with Active Directory.</summary>
internal static partial class DirectoryValues
{
    /// <summary>The longest user logon name (pre-Windows 2000) AD allows.</summary>
    public const int SamAccountNameMaxLength = 20;

    /// <summary>
    /// Turns what the user typed into a sAMAccountName: either the name itself or <c>name@Domain</c> for the
    /// configured domain. Anything else (another domain, characters AD forbids in logon names, wildcards) is
    /// refused before the directory is contacted.
    /// </summary>
    public static bool TryGetSamAccountName(string input, string domain, out string samAccountName)
    {
        samAccountName = string.Empty;
        var name = input.Trim();
        var at = name.LastIndexOf('@');
        if (at >= 0)
        {
            if (!name[(at + 1)..].Equals(domain, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            name = name[..at];
        }

        if (!SamAccountName().IsMatch(name) || name.Trim('.', ' ').Length == 0)
        {
            return false;
        }

        samAccountName = name;
        return true;
    }

    /// <summary>Escapes a value for use inside an LDAP search filter (RFC 4515).</summary>
    public static string EscapeFilterValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            _ = c switch
            {
                '\\' => builder.Append(@"\5c"),
                '*' => builder.Append(@"\2a"),
                '(' => builder.Append(@"\28"),
                ')' => builder.Append(@"\29"),
                '\0' => builder.Append(@"\00"),
                _ => builder.Append(c),
            };
        }

        return builder.ToString();
    }

    /// <summary>Escapes binary data (e.g. an objectSid) for an equality match in an LDAP filter.</summary>
    public static string EscapeFilterBytes(ReadOnlySpan<byte> value)
    {
        var builder = new StringBuilder(value.Length * 3);
        foreach (var b in value)
        {
            builder.Append('\\').Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>Converts a binary SID (as stored in objectSid and tokenGroups) to its S-1-… form.</summary>
    public static string SidToString(ReadOnlySpan<byte> sid)
    {
        if (sid.Length < 8 || sid.Length != 8 + (sid[1] * 4))
        {
            throw new FormatException("The value is not a binary security identifier.");
        }

        long authority = 0;
        for (var i = 2; i < 8; i++)
        {
            authority = (authority << 8) | sid[i];
        }

        var builder = new StringBuilder("S-").Append(sid[0]).Append('-').Append(authority);
        for (var i = 0; i < sid[1]; i++)
        {
            builder.Append('-').Append(BinaryPrimitives.ReadUInt32LittleEndian(sid.Slice(8 + (i * 4), 4)));
        }

        return builder.ToString();
    }

    /// <summary>Converts an S-1-… SID to the binary form AD stores and filters on.</summary>
    public static byte[] SidToBytes(string sid)
    {
        var parts = sid.Split('-');
        if (parts.Length < 3 || parts[0] != "S" || parts.Length - 3 > 15)
        {
            throw new FormatException($"'{sid}' is not a security identifier.");
        }

        var revision = byte.Parse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture);
        var authority = long.Parse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture);
        var subAuthorities = parts[3..].Select(p => uint.Parse(p, NumberStyles.None, CultureInfo.InvariantCulture)).ToArray();

        var bytes = new byte[8 + (subAuthorities.Length * 4)];
        bytes[0] = revision;
        bytes[1] = (byte)subAuthorities.Length;
        for (var i = 7; i >= 2; i--)
        {
            bytes[i] = (byte)(authority & 0xFF);
            authority >>= 8;
        }

        for (var i = 0; i < subAuthorities.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8 + (i * 4)), subAuthorities[i]);
        }

        return bytes;
    }

    /// <summary>The relative identifier (last part) of a SID, and the domain SID before it.</summary>
    public static (string DomainSid, uint Rid) SplitRid(string sid)
    {
        var dash = sid.LastIndexOf('-');
        return (sid[..dash], uint.Parse(sid[(dash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture));
    }

    // AD forbids " / \ [ ] : ; | = , + * ? < > @ and control characters in logon names.
    [GeneratedRegex(@"^[^""/\\\[\]:;|=,+*?<>@\p{Cc}]{1,20}$")]
    private static partial Regex SamAccountName();
}
