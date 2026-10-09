using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EnterpriseInventory.IntegrationTests.ActiveDirectory;

namespace EnterpriseInventory.IntegrationTests.Api;

/// <summary>
/// <c>POST /api/auth/login</c> through the whole pipeline. These tests need no database: every request here is
/// refused before a sign-in would be recorded. Successful sign-ins are in <see cref="SignInRecordingTests"/>.
/// </summary>
public class LoginEndpointTests
{
    internal static readonly Uri Login = new("/api/auth/login", UriKind.Relative);

    [Fact]
    public async Task Missing_fields_get_turkish_validation_messages_without_contacting_the_directory()
    {
        await using var api = new TestApiFactory(useTestAuthentication: false);
        using var client = api.CreateAnonymousClient();

        using var response = await client.PostAsJsonAsync(Login, new { userName = " ", password = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ProblemJson.ReadAsync(response);
        Assert.Equal("İstek geçersiz.", problem.GetProperty("title").GetString());
        var errors = problem.GetProperty("errors");
        Assert.Equal("Kullanıcı adı zorunludur.", errors.GetProperty("userName")[0].GetString());
        Assert.Equal("Parola zorunludur.", errors.GetProperty("password")[0].GetString());
    }

    [Fact]
    public async Task A_form_post_cannot_sign_in()
    {
        // The endpoint only accepts JSON, which a cross-site form cannot send. A request no endpoint accepts falls
        // to the deny-by-default policy, so it is refused as unauthenticated before any media type check.
        await using var api = new TestApiFactory(useTestAuthentication: false);
        using var client = api.CreateAnonymousClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["userName"] = "ayse.admin", ["password"] = "x" });

        using var response = await client.PostAsync(Login, form);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Oversized_bodies_are_refused()
    {
        // The in-memory test server does not enforce body size limits, so this one runs on Kestrel.
        await using var api = new TestApiFactory(useTestAuthentication: false);
        api.UseKestrel(0);
        api.StartServer();
        using var client = api.CreateClient();
        using var body = new StringContent(
            JsonSerializer.Serialize(new { userName = "ayse.admin", password = new string('x', 10_000) }), Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(Login, body);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task An_unreachable_directory_refuses_sign_in()
    {
        await using var api = new TestApiFactory(useTestAuthentication: false);
        using var client = api.CreateAnonymousClient();

        using var response = await client.PostAsJsonAsync(Login, new { userName = "ayse.admin", password = "Any-Password-1" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await ProblemJson.ReadAsync(response);
        Assert.Equal("Giriş şu anda yapılamıyor.", problem.GetProperty("title").GetString());
        Assert.Equal("directory_unavailable", problem.GetProperty("code").GetString());
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Sign_in_attempts_are_rate_limited_per_client_address()
    {
        await using var api = new TestApiFactory(
            useTestAuthentication: false,
            settings: new Dictionary<string, string?> { ["RateLimiting:LoginPermitLimit"] = "2" });
        using var client = api.CreateAnonymousClient();

        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            last?.Dispose();
            last = await client.PostAsJsonAsync(Login, new { userName = "ayse.admin", password = "" });
            statuses.Add(last.StatusCode);
        }

        Assert.Equal([HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.TooManyRequests], statuses);
        Assert.True(last!.Headers.RetryAfter is not null);
        last.Dispose();
    }

    [Fact]
    public async Task Passwords_never_reach_the_log()
    {
        const string password = "Sizmamali-Parola-7f3a";
        var settings = new Dictionary<string, string?>(FakeDirectory)
        {
            // The password is right for the non-member and the disabled member, so they are refused only after the
            // password check.
            ["ActiveDirectory:FakeUsers:1:Password"] = password,
            ["ActiveDirectory:FakeUsers:2:Password"] = password,
        };

        var log = await SignInWithFileLogging(settings, [("dev.admin", password), ("dev.user", password), ("dev.disabled", password), ("nobody", password)]);

        Assert.Contains("dev.user", log, StringComparison.Ordinal);
        Assert.DoesNotContain(password, log, StringComparison.Ordinal);
    }

    [ActiveDirectoryFact]
    public async Task Passwords_never_reach_the_log_with_the_test_directory()
    {
        const string wrongPassword = "Sizmamali-Yanlis-Parola-41c9";
        var password = TestActiveDirectory.UserPassword;

        // A member (accepted by the directory, then refused because the test database is unreachable), a
        // non-member, a disabled member, an unknown user, a logon name another account's UPN claims, and a wrong password.
        var log = await SignInWithFileLogging(
            TestActiveDirectory.Settings(),
            [("ayse.admin", password), ("mehmet.user", password), ("disabled.user", password), ("nobody.here", password),
             ("clash.member", password), ("ayse.admin", wrongPassword)]);

        Assert.Contains("mehmet.user", log, StringComparison.Ordinal);
        Assert.Contains("could not be recorded", log, StringComparison.Ordinal);
        Assert.DoesNotContain(password, log, StringComparison.Ordinal);
        Assert.DoesNotContain(wrongPassword, log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sign_in_that_cannot_be_recorded_starts_no_session()
    {
        // The directory accepts dev.admin; the database cannot be reached.
        await using var api = new TestApiFactory(useTestAuthentication: false, settings: FakeDirectory);
        using var client = api.CreateAnonymousClient();

        using var response = await client.PostAsJsonAsync(Login, new { userName = "dev.admin", password = FakePassword });

        await AssertRefused(response, HttpStatusCode.ServiceUnavailable, "sign_in_unavailable", "Giriş şu anda yapılamıyor.");
    }

    [Theory]
    [InlineData("dev.admin", "wrong-password", HttpStatusCode.Unauthorized, "invalid_credentials", "Kullanıcı adı veya parola hatalı.")]
    [InlineData("nobody", FakePassword, HttpStatusCode.Unauthorized, "invalid_credentials", "Kullanıcı adı veya parola hatalı.")]
    [InlineData("dev.user", FakePassword, HttpStatusCode.Forbidden, "not_authorized", "Bu uygulamaya giriş yetkiniz yok.")]
    [InlineData("dev.disabled", FakePassword, HttpStatusCode.Forbidden, "account_unavailable", "Hesabınızla şu anda giriş yapılamıyor.")]
    public async Task Development_fake_directory_refusals(string userName, string password, HttpStatusCode status, string code, string title)
    {
        await using var api = new TestApiFactory(useTestAuthentication: false, settings: FakeDirectory);
        using var client = api.CreateAnonymousClient();

        using var response = await client.PostAsJsonAsync(Login, new { userName, password });

        await AssertRefused(response, status, code, title);
    }

    [ActiveDirectoryTheory]
    [InlineData("ayse.admin", false, HttpStatusCode.Unauthorized, "invalid_credentials")]
    [InlineData("nobody.here", true, HttpStatusCode.Unauthorized, "invalid_credentials")]
    [InlineData("mehmet.user", true, HttpStatusCode.Forbidden, "not_authorized")]
    [InlineData("decoy.user", true, HttpStatusCode.Forbidden, "not_authorized")]
    [InlineData("disabled.user", true, HttpStatusCode.Forbidden, "account_unavailable")]
    [InlineData("disabled.user", false, HttpStatusCode.Unauthorized, "invalid_credentials")]
    [InlineData("clash.member", true, HttpStatusCode.Unauthorized, "invalid_credentials")]
    public async Task Test_directory_refusals(string userName, bool rightPassword, HttpStatusCode status, string code)
    {
        await using var api = new TestApiFactory(useTestAuthentication: false, settings: TestActiveDirectory.Settings());
        using var client = api.CreateAnonymousClient();

        using var response = await client.PostAsJsonAsync(
            Login, new { userName, password = rightPassword ? TestActiveDirectory.UserPassword : "Wrong-Password-1" });

        await AssertRefused(response, status, code, title: null);
    }

    internal const string FakePassword = "dev-only-password";

    /// <summary>The Development-only fake directory with a member, a non-member and a disabled member.</summary>
    internal static readonly IReadOnlyDictionary<string, string?> FakeDirectory = new Dictionary<string, string?>
    {
        ["ActiveDirectory:Mode"] = "Fake",
        ["ActiveDirectory:FakeUsers:0:UserName"] = "dev.admin",
        ["ActiveDirectory:FakeUsers:0:Password"] = FakePassword,
        ["ActiveDirectory:FakeUsers:0:DisplayName"] = "Geliştirici Yönetici",
        ["ActiveDirectory:FakeUsers:1:UserName"] = "dev.user",
        ["ActiveDirectory:FakeUsers:1:Password"] = FakePassword,
        ["ActiveDirectory:FakeUsers:1:IsAllowedGroupMember"] = "false",
        ["ActiveDirectory:FakeUsers:2:UserName"] = "dev.disabled",
        ["ActiveDirectory:FakeUsers:2:Password"] = FakePassword,
        ["ActiveDirectory:FakeUsers:2:IsDisabled"] = "true",
    };

    /// <summary>Signs in with each user name and password while every log level is written to a file; returns the file.</summary>
    private static async Task<string> SignInWithFileLogging(
        IReadOnlyDictionary<string, string?> settings, IReadOnlyList<(string UserName, string Password)> attempts)
    {
        var logFile = Path.Combine(Path.GetTempPath(), $"ei-login-{Guid.NewGuid():N}.log");
        try
        {
            await using (var api = new TestApiFactory(
                useTestAuthentication: false,
                settings: new Dictionary<string, string?>(settings)
                {
                    ["Serilog:MinimumLevel:Default"] = "Verbose",
                    ["Serilog:MinimumLevel:Override:Microsoft.AspNetCore"] = "Verbose",
                    ["Serilog:WriteTo:0:Name"] = "File",
                    ["Serilog:WriteTo:0:Args:path"] = logFile,
                    ["Serilog:WriteTo:0:Args:outputTemplate"] = "{Message:lj} {Properties:j}{NewLine}{Exception}",
                }))
            {
                using var client = api.CreateAnonymousClient();
                foreach (var (userName, password) in attempts)
                {
                    using var response = await client.PostAsJsonAsync(Login, new { userName, password });
                    Assert.True(
                        response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.ServiceUnavailable,
                        $"{userName}: {response.StatusCode}");
                }
            }

            return await File.ReadAllTextAsync(logFile);
        }
        finally
        {
            File.Delete(logFile);
        }
    }

    private static async Task AssertRefused(HttpResponseMessage response, HttpStatusCode status, string code, string? title)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await ProblemJson.ReadAsync(response);
        Assert.Equal(code, problem.GetProperty("code").GetString());
        if (title is not null)
        {
            Assert.Equal(title, problem.GetProperty("title").GetString());
        }

        Assert.False(response.Headers.Contains("Set-Cookie"));
    }
}
