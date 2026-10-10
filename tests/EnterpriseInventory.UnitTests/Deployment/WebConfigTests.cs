using System.Xml.Linq;

namespace EnterpriseInventory.UnitTests.Deployment;

/// <summary>
/// The IIS settings published with the API (<c>src/EnterpriseInventory.Api/web.config</c>): in-process hosting in
/// Production, IIS leaving the API's own error answers alone, and no secret or other setting in the file.
/// </summary>
public class WebConfigTests
{
    private static readonly Lazy<XElement> WebServer = new(() =>
        XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "EnterpriseInventory.Api", "web.config"))
            .Root!.Element("location")!.Element("system.webServer")!);

    [Fact]
    public void The_api_runs_in_process_behind_the_asp_net_core_module_v2()
    {
        var handler = Assert.Single(WebServer.Value.Element("handlers")!.Elements("add"));
        var module = WebServer.Value.Element("aspNetCore")!;

        Assert.Equal("AspNetCoreModuleV2", handler.Attribute("modules")?.Value);
        Assert.Equal("inprocess", module.Attribute("hostingModel")?.Value);
        Assert.Equal(".\\EnterpriseInventory.Api.dll", module.Attribute("arguments")?.Value);
    }

    [Fact]
    public void Only_the_environment_name_is_set_and_it_is_production()
    {
        // Secrets and other settings go into the site's environment variables in IIS, never into this file.
        var variable = Assert.Single(WebServer.Value.Element("aspNetCore")!.Element("environmentVariables")!.Elements());

        Assert.Equal("ASPNETCORE_ENVIRONMENT", variable.Attribute("name")?.Value);
        Assert.Equal("Production", variable.Attribute("value")?.Value);
        Assert.DoesNotContain(WebServer.Value.Document!.Descendants(), e => e.Name.LocalName is "connectionStrings" or "appSettings");
    }

    [Fact]
    public void Standard_output_logging_is_off()
    {
        // Serilog writes the log; stdout files would grow without limit and are only for start-up troubleshooting.
        Assert.Equal("false", WebServer.Value.Element("aspNetCore")!.Attribute("stdoutLogEnabled")?.Value);
    }

    [Fact]
    public void Iis_passes_the_apis_own_error_answers_through_and_names_no_server()
    {
        var filtering = WebServer.Value.Element("security")!.Element("requestFiltering")!;

        Assert.Equal("PassThrough", WebServer.Value.Element("httpErrors")?.Attribute("existingResponse")?.Value);
        Assert.Equal("true", filtering.Attribute("removeServerHeader")?.Value);
        Assert.Equal("1048576", filtering.Element("requestLimits")?.Attribute("maxAllowedContentLength")?.Value);
        Assert.Contains(
            WebServer.Value.Element("httpProtocol")!.Element("customHeaders")!.Elements("remove"),
            remove => remove.Attribute("name")?.Value == "X-Powered-By");
    }

    [Fact]
    public void Development_settings_are_never_published()
    {
        var project = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "EnterpriseInventory.Api", "EnterpriseInventory.Api.csproj"));

        Assert.Contains(
            project.Descendants("Content"),
            content => content.Attribute("Update")?.Value == "appsettings.Development.json" && content.Attribute("CopyToPublishDirectory")?.Value == "Never");
    }

    internal static string FindRepositoryRoot()
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
