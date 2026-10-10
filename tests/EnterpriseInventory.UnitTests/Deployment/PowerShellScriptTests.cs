namespace EnterpriseInventory.UnitTests.Deployment;

/// <summary>
/// The IIS scripts in <c>deploy/iis</c>. Windows PowerShell 5.1 reads a script without a byte order mark in the
/// server's ANSI code page, which garbles the Turkish messages; and no script may offer a way around certificate
/// validation (the LDAPS check's own callback accepts only a certificate with no errors).
/// </summary>
public class PowerShellScriptTests
{
    private static readonly string ScriptDirectory = Path.Combine(WebConfigTests.FindRepositoryRoot(), "deploy", "iis");

    private static readonly string[] ScriptNames =
        [.. Directory.GetFiles(ScriptDirectory, "*.ps1").Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];

    public static TheoryData<string> Scripts() => new(ScriptNames);

    [Fact]
    public void The_four_deployment_scripts_are_there()
    {
        Assert.Equal(
            ["Install-EnterpriseInventorySite.ps1", "Publish-EnterpriseInventory.ps1", "Test-Deployment.ps1", "Test-ServerPrerequisites.ps1"],
            ScriptNames);
    }

    [Theory]
    [MemberData(nameof(Scripts))]
    public void Each_script_is_saved_as_utf8_with_a_byte_order_mark(string script)
    {
        var bytes = File.ReadAllBytes(Path.Combine(ScriptDirectory, script));

        Assert.True(bytes is [0xEF, 0xBB, 0xBF, ..], $"{script} has no UTF-8 byte order mark.");
    }

    [Theory]
    [MemberData(nameof(Scripts))]
    public void No_script_turns_certificate_validation_off(string script)
    {
        var text = File.ReadAllText(Path.Combine(ScriptDirectory, script));

        Assert.DoesNotContain("SkipCertificateCheck", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ServerCertificateValidationCallback", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ServerCertificateCustomValidationCallback", text, StringComparison.OrdinalIgnoreCase);
    }
}
