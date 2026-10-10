using System.Diagnostics;
using System.Text.RegularExpressions;

namespace EnterpriseInventory.UnitTests.Architecture;

/// <summary>
/// What the repository must never hold: keys and certificates, passwords in configuration, deployment files or
/// documents, and settings that switch off certificate validation. Reads the files Git tracks, so local secrets
/// kept out of Git (user-secrets, .env) do not count.
/// </summary>
public sealed partial class RepositorySecretsTests
{
    private static readonly Lazy<(string Root, IReadOnlyList<string> Files)> Tracked = new(ReadTrackedFiles);

    [Fact]
    public void The_scan_reads_the_repository()
    {
        // A guard for the tests below, which pass trivially on an empty list.
        Assert.Contains("src/EnterpriseInventory.Api/appsettings.Production.json", Tracked.Value.Files);
        Assert.True(Tracked.Value.Files.Count > 300, $"Only {Tracked.Value.Files.Count} files were found.");
    }

    [Fact]
    public void No_private_key_or_certificate_file_is_committed()
    {
        var keyFiles = Tracked.Value.Files.Where(path => KeyFile().IsMatch(path));
        var keyBlocks = TextFiles().Where(file => PrivateKeyBlock().IsMatch(file.Text)).Select(file => file.Path);

        Assert.Empty(keyFiles);
        Assert.Empty(keyBlocks);
    }

    [Fact]
    public void Configuration_deployment_files_and_documents_hold_no_password()
    {
        // "Password=<placeholder>" is how documents show a connection string; anything else is a value.
        var found = TextFiles()
            .Where(file => ConfigurationFile().IsMatch(file.Path))
            .SelectMany(file => PasswordValue().Matches(file.Text).Select(match => $"{file.Path}: {match.Value}"));

        Assert.Empty(found);
    }

    [Fact]
    public void Nothing_outside_the_tests_switches_off_certificate_validation()
    {
        var found = TextFiles()
            .Where(file => !IsTest(file.Path))
            .SelectMany(file => CertificateBypass().Matches(file.Text).Select(match => $"{file.Path}: {match.Value}"));

        Assert.Empty(found);
    }

    private static bool IsTest(string path) =>
        path.StartsWith("tests/", StringComparison.Ordinal)
        || path.Contains("/e2e/", StringComparison.Ordinal)
        || path.Contains(".test.", StringComparison.Ordinal)
        || path.EndsWith("playwright.config.ts", StringComparison.Ordinal);

    private static IEnumerable<(string Path, string Text)> TextFiles()
    {
        var (root, files) = Tracked.Value;
        return files
            .Where(path => TextFile().IsMatch(path) && !path.EndsWith("package-lock.json", StringComparison.Ordinal))
            .Select(path => (path, File.ReadAllText(Path.Combine(root, path))));
    }

    private static (string Root, IReadOnlyList<string> Files) ReadTrackedFiles()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EnterpriseInventory.slnx")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
        using var git = Process.Start(new ProcessStartInfo("git", ["-C", root, "ls-files", "-z"])
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("git could not be started.");
        var output = git.StandardOutput.ReadToEnd();
        git.WaitForExit();
        Assert.Equal(0, git.ExitCode);
        return (root, output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(path => File.Exists(Path.Combine(root, path))).ToList());
    }

    [GeneratedRegex(@"\.(pfx|p12|pem|key|snk|jks|keystore)$", RegexOptions.IgnoreCase)]
    private static partial Regex KeyFile();

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----")]
    private static partial Regex PrivateKeyBlock();

    [GeneratedRegex(@"\.(cs|ts|tsx|js|mjs|json|config|xml|props|targets|ps1|psm1|sh|sql|md|yml|yaml|ini|txt|example|env)$", RegexOptions.IgnoreCase)]
    private static partial Regex TextFile();

    [GeneratedRegex(@"(\.(json|config|xml|ps1|psm1|sh|sql|md|yml|yaml|ini|txt|example|env)$|(^|/)\.env)", RegexOptions.IgnoreCase)]
    private static partial Regex ConfigurationFile();

    [GeneratedRegex(@"\b(password|pwd)\s*=\s*(?!<)[^;""'\s<>`]+", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordValue();

    [GeneratedRegex(
        @"TrustServerCertificate\s*=\s*(true|yes)|Encrypt\s*=\s*(false|no|optional)|DangerousAcceptAnyServerCertificateValidator|NODE_TLS_REJECT_UNAUTHORIZED|rejectUnauthorized\s*:\s*false|secure\s*:\s*false",
        RegexOptions.IgnoreCase)]
    private static partial Regex CertificateBypass();
}
