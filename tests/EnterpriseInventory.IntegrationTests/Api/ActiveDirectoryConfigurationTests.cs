using System.Net;
using System.Text.Json;
using EnterpriseInventory.Infrastructure;
using EnterpriseInventory.Infrastructure.ActiveDirectory;
using EnterpriseInventory.IntegrationTests.ActiveDirectory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;

namespace EnterpriseInventory.IntegrationTests.Api;

/// <summary>How the API starts (or refuses to) with directory settings, and how it reports the directory's health.</summary>
public class ActiveDirectoryConfigurationTests
{
    [Fact]
    public void Shipped_production_settings_cannot_start_until_the_placeholders_are_replaced()
    {
        var apiDirectory = Path.Combine(RepositoryRoot(), "src", "EnterpriseInventory.Api");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(apiDirectory, "appsettings.json"))
            .AddJsonFile(Path.Combine(apiDirectory, "appsettings.Production.json"))
            .Build();
        var options = configuration.GetSection(ActiveDirectoryOptions.SectionName).Get<ActiveDirectoryOptions>()!;

        var result = new ActiveDirectoryOptionsValidator(new HostingEnvironment { EnvironmentName = "Production" }).Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("ActiveDirectory:ServerFqdn still contains the CHANGE-ME placeholder", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("ActiveDirectory:AllowedGroupSid still contains the CHANGE-ME placeholder", result.FailureMessage, StringComparison.Ordinal);
        Assert.Equal(DirectoryMode.Ldap, options.Mode);
        Assert.True(options.UseLdaps);
        Assert.True(options.CheckCertificateRevocation);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Fake_directory_stops_the_application_outside_development(string environment)
    {
        await using var api = new TestApiFactory(
            environment: environment,
            settings: new Dictionary<string, string?> { ["ActiveDirectory:Mode"] = "Fake" });

        var error = Assert.Throws<OptionsValidationException>(() => api.CreateAnonymousClient());

        Assert.Contains("'Fake' is only allowed in Development", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Plain_ldap_stops_the_application()
    {
        await using var api = new TestApiFactory(
            environment: "Production",
            settings: new Dictionary<string, string?> { ["ActiveDirectory:UseLdaps"] = "false", ["ActiveDirectory:Port"] = "389" });

        var error = Assert.Throws<OptionsValidationException>(() => api.CreateAnonymousClient());

        Assert.Contains("UseLdaps must be true", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Development_can_run_with_the_fake_directory_and_no_server_settings()
    {
        await using var api = new TestApiFactory(settings: new Dictionary<string, string?>
        {
            ["ActiveDirectory:Mode"] = "Fake",
            ["ActiveDirectory:ServerFqdn"] = "",
            ["ActiveDirectory:AllowedGroupSid"] = "",
        });
        using var client = api.CreateSignedInClient();

        using var response = await client.GetAsync(new Uri("/api/health", UriKind.Relative));
        var report = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        var directory = HealthEndpointTests.HealthCheck(report, "active-directory");
        Assert.Equal("Healthy", directory.GetProperty("status").GetString());
        Assert.StartsWith("The Development-only fake directory is in use", directory.GetProperty("description").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unreachable_directory_degrades_health_but_not_readiness()
    {
        await using var api = new TestApiFactory();
        using var client = api.CreateSignedInClient();

        using var response = await client.GetAsync(new Uri("/api/health", UriKind.Relative));
        var report = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        var directory = HealthEndpointTests.HealthCheck(report, "active-directory");
        Assert.Equal("Degraded", directory.GetProperty("status").GetString());
        // Unreachable or timed out, depending on how fast the resolver gives up on the .invalid name.
        Assert.Contains("dc1.unreachable.invalid:636", directory.GetProperty("description").GetString(), StringComparison.Ordinal);

        var registration = api.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Single(r => r.Name == "active-directory");
        Assert.DoesNotContain(HealthCheckTags.Ready, registration.Tags);
    }

    [ActiveDirectoryFact]
    public async Task Health_reports_the_test_domain_controller_as_healthy()
    {
        await using var api = new TestApiFactory(settings: TestActiveDirectory.Settings());
        using var client = api.CreateSignedInClient();

        using var response = await client.GetAsync(new Uri("/api/health", UriKind.Relative));
        var report = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        var directory = HealthEndpointTests.HealthCheck(report, "active-directory");
        Assert.Equal("Healthy", directory.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); // the database is unreachable here
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EnterpriseInventory.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
