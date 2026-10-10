using System.Text.Json;
using System.Xml.Linq;
using EnterpriseInventory.UnitTests.Deployment;

namespace EnterpriseInventory.UnitTests.Architecture;

/// <summary>
/// The files Visual Studio opens the project with: the solution lists the React app too, the shared launch profile
/// starts the API and the React dev server together, and the dev server's proxy reaches the API profile it starts.
/// </summary>
public class VisualStudioSolutionTests
{
    private static readonly string Root = WebConfigTests.FindRepositoryRoot();

    [Fact]
    public void The_solution_lists_every_project_including_the_react_app()
    {
        var listed = SolutionProjects();
        string[] expected =
            [
                "src/EnterpriseInventory.Api/EnterpriseInventory.Api.csproj",
                "src/EnterpriseInventory.Application/EnterpriseInventory.Application.csproj",
                "src/EnterpriseInventory.Domain/EnterpriseInventory.Domain.csproj",
                "src/EnterpriseInventory.Infrastructure/EnterpriseInventory.Infrastructure.csproj",
                "src/EnterpriseInventory.Web/EnterpriseInventory.Web.esproj",
                "tests/EnterpriseInventory.IntegrationTests/EnterpriseInventory.IntegrationTests.csproj",
                "tests/EnterpriseInventory.UnitTests/EnterpriseInventory.UnitTests.csproj",
            ];

        Assert.Equal(expected, listed.Order(StringComparer.Ordinal));
        Assert.All(listed, path => Assert.True(File.Exists(Path.Combine(Root, path)), $"{path} does not exist."));
    }

    [Fact]
    public void The_shared_launch_profile_starts_the_api_and_the_react_app_from_the_solution()
    {
        using var launch = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "EnterpriseInventory.slnLaunch")));
        var profile = Assert.Single(launch.RootElement.EnumerateArray());
        var projects = profile.GetProperty("Projects").EnumerateArray()
            .Select(p => (Path: p.GetProperty("Path").GetString()!.Replace('\\', '/'), Action: p.GetProperty("Action").GetString()))
            .ToList();

        (string Path, string? Action)[] expected =
            [
                ("src/EnterpriseInventory.Api/EnterpriseInventory.Api.csproj", "Start"),
                ("src/EnterpriseInventory.Web/EnterpriseInventory.Web.esproj", "Start"),
            ];

        Assert.Equal("API ve Web", profile.GetProperty("Name").GetString());
        Assert.Equal(expected, projects);
        Assert.All(projects, p => Assert.Contains(p.Path, SolutionProjects()));
    }

    [Fact]
    public void Building_the_solution_neither_rewrites_the_lock_file_nor_needs_the_network_for_an_audit()
    {
        // The SDK's defaults would run npm install (which may change package-lock.json), npm audit and npm run build on
        // every build. The project installs with npm ci instead, only when node_modules is missing.
        var project = XDocument.Load(Path.Combine(Root, "src", "EnterpriseInventory.Web", "EnterpriseInventory.Web.esproj")).Root!;
        string? Property(string name) => project.Descendants(name).SingleOrDefault()?.Value;

        Assert.Equal("false", Property("ShouldRunNpmInstall"));
        Assert.Equal("false", Property("ShouldRunNpmAudit"));
        Assert.Equal("false", Property("ShouldRunBuildScript"));
        Assert.Equal("npm run dev", Property("StartupCommand"));
        Assert.Contains(project.Descendants("Exec"), e => e.Attribute("Command")?.Value == "npm ci");
        Assert.DoesNotContain(project.Descendants("Exec"), e => e.Attribute("Command")?.Value == "npm install");
    }

    [Fact]
    public void The_api_profile_started_first_is_the_one_the_react_dev_server_proxies_to()
    {
        // Visual Studio and dotnet run take the first profile unless told otherwise; the Vite proxy (.env.example and
        // vite.config.ts) points at https://localhost:7261.
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "src", "EnterpriseInventory.Api", "Properties", "launchSettings.json")));
        var first = settings.RootElement.GetProperty("profiles").EnumerateObject().First();
        var proxyTarget = File.ReadAllLines(Path.Combine(Root, "src", "EnterpriseInventory.Web", ".env.example"))
            .Single(line => line.StartsWith("VITE_DEV_API_PROXY_TARGET=", StringComparison.Ordinal))
            .Split('=', 2)[1];

        Assert.Equal("https", first.Name);
        Assert.Equal("https://localhost:7261", proxyTarget);
        Assert.Contains(proxyTarget, first.Value.GetProperty("applicationUrl").GetString()!.Split(';'));
        Assert.Contains($"'{proxyTarget}'", File.ReadAllText(Path.Combine(Root, "src", "EnterpriseInventory.Web", "vite.config.ts")), StringComparison.Ordinal);
        Assert.Equal("Development", first.Value.GetProperty("environmentVariables").GetProperty("ASPNETCORE_ENVIRONMENT").GetString());
    }

    [Fact]
    public void Visual_studio_opens_the_sign_in_page_of_the_dev_server()
    {
        using var launch = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "src", "EnterpriseInventory.Web", ".vscode", "launch.json")));

        Assert.All(
            launch.RootElement.GetProperty("configurations").EnumerateArray(),
            configuration => Assert.Equal("http://localhost:5173/giris", configuration.GetProperty("url").GetString()));
    }

    private static List<string> SolutionProjects() =>
        XDocument.Load(Path.Combine(Root, "EnterpriseInventory.slnx"))
            .Descendants("Project")
            .Select(p => p.Attribute("Path")!.Value)
            .ToList();
}
