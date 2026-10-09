using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnterpriseInventory.IntegrationTests.Api;

/// <summary>
/// Hosts the real API in memory. A request carrying <see cref="TestAuthHandler.UserHeader"/> is treated as
/// signed in; challenges and forbids still go through the API's own cookie scheme. With
/// <c>useTestAuthentication: false</c> only the API's real cookie sign-in applies.
/// </summary>
public sealed class TestApiFactory(
    string? connectionString = TestApiFactory.UnreachableDatabase,
    string environment = "Development",
    IReadOnlyDictionary<string, string?>? settings = null,
    Action<IApplicationBuilder>? appendToPipeline = null,
    bool useTestAuthentication = true,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<Program>
{
    /// <summary>Data Protection keys shared by every test host, as the hosts of one deployment share theirs.</summary>
    public static readonly string KeysDirectory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ei-test-dataprotection-keys")).FullName;

    /// <summary>A server name that cannot resolve, so database checks fail fast without SQL Server.</summary>
    public const string UnreachableDatabase = "Server=unreachable.invalid;Database=none;Connect Timeout=1";

    /// <summary>
    /// Valid directory settings for a domain controller that cannot resolve (".invalid" never does), so the
    /// API starts in every environment; tests that need a directory override these keys.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string?> UnreachableDirectory = new Dictionary<string, string?>
    {
        ["ActiveDirectory:Mode"] = "Ldap",
        ["ActiveDirectory:Domain"] = "unreachable.invalid",
        ["ActiveDirectory:ServerFqdn"] = "dc1.unreachable.invalid",
        ["ActiveDirectory:BaseDn"] = "DC=unreachable,DC=invalid",
        ["ActiveDirectory:AllowedGroupSid"] = "S-1-5-21-1-2-3-1105",
        ["ActiveDirectory:NestedGroupPolicy"] = "DirectMembershipOnly",
        ["ActiveDirectory:ServiceAccountUserName"] = "svc.unreachable",
        ["ActiveDirectory:ServiceAccountPassword"] = "not-a-real-password",
        ["ActiveDirectory:ConnectTimeoutSeconds"] = "2",
    };

    public HttpClient CreateAnonymousClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    public HttpClient CreateSignedInClient(string userName = "ayse.admin", string roles = "Administrator")
    {
        var client = CreateAnonymousClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userName);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, roles);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var values = new Dictionary<string, string?>(UnreachableDirectory)
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["DataProtection:KeysDirectory"] = KeysDirectory,
            };
            foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            {
                values[key] = value;
            }

            configuration.AddInMemoryCollection(values);
        });
        builder.ConfigureTestServices(services =>
        {
            if (useTestAuthentication)
            {
                services.AddAuthentication(options => options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            }

            if (appendToPipeline is not null)
            {
                services.AddSingleton<IStartupFilter>(new AppendToPipeline(appendToPipeline));
            }

            configureServices?.Invoke(services);
        });
    }

    /// <summary>Runs <paramref name="configure"/> after the API's pipeline, i.e. for requests no endpoint handled.</summary>
    private sealed class AppendToPipeline(Action<IApplicationBuilder> configure) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            configure(app);
        };
    }
}

internal sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";
    public const string RolesHeader = "X-Test-Roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userName = Request.Headers[UserHeader].ToString();
        if (userName.Length == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = Request.Headers[RolesHeader].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(role => new Claim(ClaimTypes.Role, role))
            .Append(new Claim(ClaimTypes.Name, userName));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
