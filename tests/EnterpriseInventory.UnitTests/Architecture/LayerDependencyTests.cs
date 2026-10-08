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
