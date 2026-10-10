using System.Xml.Linq;

namespace EnterpriseInventory.UnitTests.Architecture;

/// <summary>
/// Guards the Clean Architecture dependency rule by reading the project references in each .csproj:
/// Domain &lt;- Application &lt;- Infrastructure &lt;- Api.
/// </summary>
public class LayerDependencyTests
{
    private const string Domain = "EnterpriseInventory.Domain";
    private const string Application = "EnterpriseInventory.Application";
    private const string Infrastructure = "EnterpriseInventory.Infrastructure";
    private const string Api = "EnterpriseInventory.Api";

    public static TheoryData<string, string[]> AllowedReferences => new()
    {
        { Domain, [] },
        { Application, [Domain] },
        { Infrastructure, [Application] },
        { Api, [Application, Infrastructure] },
    };

    [Theory]
    [MemberData(nameof(AllowedReferences))]
    public void Project_references_only_allowed_layers(string project, string[] expected)
    {
        var actual = ReadProjectReferences(project);

        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Domain_has_no_package_references()
    {
        var csproj = XDocument.Load(CsprojPath(Domain));

        Assert.Empty(csproj.Descendants("PackageReference"));
    }

    /// <summary>
    /// EF Core tests run on SQL Server: the InMemory and SQLite providers behave differently (no constraints,
    /// collation, row versions or transactions as SQL Server has them), so no project may use them.
    /// </summary>
    [Fact]
    public void No_project_uses_an_in_memory_or_sqlite_database_provider()
    {
        var projects = Directory.GetFiles(FindRepositoryRoot(), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(projects);
        Assert.All(projects, path => Assert.DoesNotContain(
            XDocument.Load(path).Descendants("PackageReference").Select(e => e.Attribute("Include")!.Value),
            package => package is "Microsoft.EntityFrameworkCore.InMemory" or "Microsoft.EntityFrameworkCore.Sqlite"));
    }

    /// <summary>
    /// The soft-delete filter is turned off only through SoftDelete.IncludingArchived, whose callers are the
    /// archive and history queries listed in docs/database.md.
    /// </summary>
    [Fact]
    public void Query_filters_are_ignored_only_through_SoftDelete_IncludingArchived()
    {
        var sources = Directory.GetFiles(Path.Combine(FindRepositoryRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("IgnoreQueryFilters", StringComparison.Ordinal))
            .Select(Path.GetFileName);

        Assert.Equal(["SoftDelete.cs"], sources);
    }

    private static IEnumerable<string> ReadProjectReferences(string project) =>
        XDocument.Load(CsprojPath(project))
            .Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include")!.Value.Replace('\\', '/')));

    private static string CsprojPath(string project) =>
        Path.Combine(FindRepositoryRoot(), "src", project, $"{project}.csproj");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EnterpriseInventory.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("EnterpriseInventory.slnx was not found above the test output directory.");
    }
}
