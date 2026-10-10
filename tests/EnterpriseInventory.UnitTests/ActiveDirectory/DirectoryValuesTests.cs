using EnterpriseInventory.Application.Authentication;
using EnterpriseInventory.Infrastructure.ActiveDirectory;

namespace EnterpriseInventory.UnitTests.ActiveDirectory;

public class DirectoryValuesTests
{
    private const string Domain = "corp.example.com";

    [Theory]
    [InlineData("ayse.yilmaz", "ayse.yilmaz")]
    [InlineData("  ayse.yilmaz  ", "ayse.yilmaz")]
    [InlineData("ayse.yilmaz@corp.example.com", "ayse.yilmaz")]
    [InlineData("ayse.yilmaz@CORP.EXAMPLE.COM", "ayse.yilmaz")]
    [InlineData("şükrü.öz", "şükrü.öz")]
    [InlineData("a-b_c$", "a-b_c$")]
    public void Logon_names_and_upns_of_the_domain_are_accepted(string input, string expected)
    {
        Assert.True(DirectoryValues.TryGetSamAccountName(input, Domain, out var sam));
        Assert.Equal(expected, sam);
    }

    [Theory]
    [InlineData("ayse.yilmaz@other.example.com")]
    [InlineData("ayse@yilmaz@corp")]
    [InlineData("CORP\\ayse.yilmaz")]
    [InlineData("*")]
    [InlineData("ayse*")]
    [InlineData("ayse)(objectClass=*")]
    [InlineData("a,b")]
    [InlineData("a=b")]
    [InlineData("tab\tname")]
    [InlineData("averyveryverylongname1")]
    [InlineData("...")]
    [InlineData("")]
    [InlineData("@corp.example.com")]
    public void Anything_else_is_refused_before_the_directory_is_asked(string input)
    {
        Assert.False(DirectoryValues.TryGetSamAccountName(input, Domain, out _));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a*b", @"a\2ab")]
    [InlineData("(x)", @"\28x\29")]
    [InlineData(@"back\slash", @"back\5cslash")]
    [InlineData("nul\0", @"nul\00")]
    public void Filter_values_are_escaped(string value, string expected)
    {
        Assert.Equal(expected, DirectoryValues.EscapeFilterValue(value));
    }

    [Fact]
    public void Binary_filter_values_are_escaped_byte_by_byte()
    {
        Assert.Equal(@"\01\05\ff", DirectoryValues.EscapeFilterBytes([0x01, 0x05, 0xFF]));
    }

    [Fact]
    public void Well_known_sid_round_trips()
    {
        // BUILTIN\Administrators, as documented for the binary SID layout.
        byte[] binary = [0x01, 0x02, 0, 0, 0, 0, 0, 0x05, 0x20, 0, 0, 0, 0x20, 0x02, 0, 0];

        Assert.Equal("S-1-5-32-544", DirectoryValues.SidToString(binary));
        Assert.Equal(binary, DirectoryValues.SidToBytes("S-1-5-32-544"));
    }

    [Fact]
    public void Domain_sid_from_the_directory_is_decoded()
    {
        // objectSid of a test user as returned over LDAP (base64).
        var binary = Convert.FromBase64String("AQUAAAAAAAUVAAAAKCJXbscJpi3GHzj6UQQAAA==");

        var sid = DirectoryValues.SidToString(binary);

        Assert.Equal("S-1-5-21-1851204136-765856199-4197982150-1105", sid);
        Assert.Equal(binary, DirectoryValues.SidToBytes(sid));
        Assert.Equal(("S-1-5-21-1851204136-765856199-4197982150", 1105u), DirectoryValues.SplitRid(sid));
    }

    [Theory]
    [InlineData(new byte[] { 1 })]
    [InlineData(new byte[] { 1, 2, 0, 0, 0, 0, 0, 5, 32, 0, 0, 0 })]
    public void Truncated_binary_sid_is_refused(byte[] binary)
    {
        Assert.Throws<FormatException>(() => DirectoryValues.SidToString(binary));
    }

    [Theory]
    [InlineData("80090308: LdapErr: DSID-0C09044E, comment: AcceptSecurityContext error, data 52e, v4563", DirectorySignInStatus.InvalidCredentials)]
    [InlineData("80090308: LdapErr: DSID-0C09044E, comment: AcceptSecurityContext error, data 525, v4563", DirectorySignInStatus.InvalidCredentials)]
    [InlineData("80090308: LdapErr: DSID-0C0903A9, comment: AcceptSecurityContext error, data 533, v1db1", DirectorySignInStatus.AccountDisabled)]
    [InlineData("80090308: LdapErr: DSID-0C09044E, comment: AcceptSecurityContext error, data 775, v4563", DirectorySignInStatus.AccountLocked)]
    [InlineData("80090308: LdapErr: DSID-0C09044E, comment: AcceptSecurityContext error, data 701, v4563", DirectorySignInStatus.AccountExpired)]
    [InlineData("80090308: LdapErr: DSID-0C09044E, comment: AcceptSecurityContext error, data 532, v4563", DirectorySignInStatus.PasswordExpired)]
    [InlineData("80090308: LdapErr: DSID-0C09044E, comment: AcceptSecurityContext error, data 773, v4563", DirectorySignInStatus.PasswordMustChange)]
    [InlineData("80090308: LdapErr: DSID-0C09044E, comment: AcceptSecurityContext error, data 530, v4563", DirectorySignInStatus.LogonNotPermitted)]
    [InlineData("80090308: LdapErr: DSID-0C09044E, comment: AcceptSecurityContext error, data 531, v4563", DirectorySignInStatus.LogonNotPermitted)]
    [InlineData("80090308: LdapErr: DSID-0C09044E, comment: AcceptSecurityContext error, data 999, v4563", DirectorySignInStatus.InvalidCredentials)]
    [InlineData("Invalid credentials", DirectorySignInStatus.InvalidCredentials)]
    [InlineData(null, DirectorySignInStatus.InvalidCredentials)]
    public void Bind_refusals_are_read_from_the_directory_message(string? message, DirectorySignInStatus expected)
    {
        Assert.Equal(expected, BindFailures.FromDiagnosticMessage(message));
    }
}
