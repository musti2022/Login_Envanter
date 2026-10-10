using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace EnterpriseInventory.UnitTests.ActiveDirectory;

internal sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;

    public string ApplicationName { get; set; } = "EnterpriseInventory.Api";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
