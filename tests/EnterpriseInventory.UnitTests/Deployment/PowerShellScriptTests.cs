namespace EnterpriseInventory.UnitTests.Deployment;

/// <summary>
/// The PowerShell scripts and modules under <c>deploy</c> (IIS and database). Windows PowerShell 5.1 reads a script
/// without a byte order mark in the server's ANSI code page, which garbles the Turkish messages; and no script may offer
/// a way around certificate validation (the LDAPS check's own callback accepts only a certificate with no errors).
/// </summary>
public class PowerShellScriptTests
{
    private static readonly string DeployDirectory = Path.Combine(WebConfigTests.FindRepositoryRoot(), "deploy");

    private static readonly string[] ScriptNames =
    [
        .. Directory.EnumerateFiles(DeployDirectory, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".ps1", StringComparison.Ordinal) || path.EndsWith(".psm1", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(DeployDirectory, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal),
    ];

    public static TheoryData<string> Scripts() => new(ScriptNames);

    [Fact]
    public void The_deployment_scripts_are_there()
    {
        Assert.Equal(
            [
                "iis/Install-EnterpriseInventorySite.ps1",
                "iis/Publish-EnterpriseInventory.ps1",
                "iis/Test-Deployment.ps1",
                "iis/Test-ServerPrerequisites.ps1",
                "sql/Restore-EnterpriseInventoryDatabase.ps1",
                "sql/SqlDeployment.psm1",
                "sql/Update-EnterpriseInventoryDatabase.ps1",
            ],
            ScriptNames);
    }

    [Theory]
    [MemberData(nameof(Scripts))]
    public void Each_script_is_saved_as_utf8_with_a_byte_order_mark(string script)
    {
        var bytes = File.ReadAllBytes(Path.Combine(DeployDirectory, script));

        Assert.True(bytes is [0xEF, 0xBB, 0xBF, ..], $"{script} has no UTF-8 byte order mark.");
    }

    [Theory]
    [MemberData(nameof(Scripts))]
    public void No_script_turns_certificate_validation_off(string script)
    {
        var text = File.ReadAllText(Path.Combine(DeployDirectory, script));

        Assert.DoesNotContain("SkipCertificateCheck", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ServerCertificateValidationCallback", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ServerCertificateCustomValidationCallback", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_database_scripts_run_sqlcmd_only_over_an_encrypted_connection_with_the_certificate_checked()
    {
        // go-sqlcmd does not encrypt unless asked; -C (trust the server certificate) is never passed.
        var module = File.ReadAllText(Path.Combine(DeployDirectory, "sql", "SqlDeployment.psm1"));

        Assert.Contains("@('-S', $Session.Server, '-Nm', '-b', '-I')", module, StringComparison.Ordinal);
        Assert.DoesNotContain("'-C'", module, StringComparison.Ordinal);
        Assert.DoesNotContain("'-No'", module, StringComparison.Ordinal);

        // The scripts call sqlcmd through the module only.
        foreach (var script in new[] { "Update-EnterpriseInventoryDatabase.ps1", "Restore-EnterpriseInventoryDatabase.ps1" })
        {
            var lines = File.ReadAllLines(Path.Combine(DeployDirectory, "sql", script));
            Assert.DoesNotContain(lines, line => line.TrimStart().StartsWith("sqlcmd", StringComparison.OrdinalIgnoreCase) || line.Contains("& sqlcmd", StringComparison.OrdinalIgnoreCase));
        }
    }
}
