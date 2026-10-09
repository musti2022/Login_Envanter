using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EnterpriseInventory.Application.Authentication;
using EnterpriseInventory.Domain.Auditing;
using EnterpriseInventory.Domain.Users;
using EnterpriseInventory.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EnterpriseInventory.IntegrationTests.Api;

/// <summary>
/// Day 9: server-side sessions behind the cookie, CSRF protection, sign-out, the idle and absolute timeouts and the
/// periodic directory re-check, through the whole API. Time is moved by a test clock; the directory is a stand-in
/// whose answer each test controls (the real directory's access check is in SambaAccessCheckTests).
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class SessionSecurityTests(SqlServerDatabaseFixture database) : IAsyncLifetime
{
    private const string SessionCookie = "__Host-EnterpriseInventory";
    private static readonly Uri Health = new("/api/health", UriKind.Relative);

    private readonly TestClock _clock = new(DateTimeOffset.UtcNow);
    private readonly ControllableDirectory _directory = new();
    private readonly List<IAsyncDisposable> _hosts = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }
    }

    // --- Cookies and identity ---------------------------------------------------------------------------------

    [Fact]
    public async Task The_csrf_cookie_is_a_host_only_secure_http_only_cookie()
    {
        await using var api = new TestApiFactory(useTestAuthentication: false);
        using var client = api.CreateAnonymousClient();

        using var response = await client.GetAsync(AuthClient.Csrf);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("__Host-EnterpriseInventory.Csrf=", cookie, StringComparison.Ordinal);
        var attributes = cookie.Split(';', StringSplitOptions.TrimEntries).Skip(1).Select(a => a.ToLowerInvariant()).ToList();
        Assert.Contains("path=/", attributes);
        Assert.Contains("secure", attributes);
        Assert.Contains("httponly", attributes);
        Assert.Contains("samesite=strict", attributes);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    [SqlServerFact]
    public async Task A_signed_in_user_reads_their_identity_and_an_anonymous_visitor_cannot()
    {
        var api = Api();
        using var client = api.CreateAnonymousClient();
        await client.SignInAsync(_directory.UserName, ControllableDirectory.Password);

        using var me = await client.GetAsync(AuthClient.Me);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var user = await me.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(_directory.UserName, user.GetProperty("userName").GetString());
        Assert.Equal("Oturum Testi", user.GetProperty("displayName").GetString());
        Assert.Equal(["Administrator"], user.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.False(user.TryGetProperty("csrfToken", out _));

        using var anonymous = api.CreateAnonymousClient();
        using var refused = await anonymous.GetAsync(AuthClient.Me);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    [SqlServerFact]
    public async Task Only_a_hash_of_the_session_key_is_stored()
    {
        var api = Api();
        using var client = api.CreateAnonymousClient();
        using var login = await client.PostLoginAsync(new { userName = _directory.UserName, password = ControllableDirectory.Password });
        var ticket = Ticket(api, CookieValue(login));

        var key = ticket.Principal.FindFirstValue("ei:session");
        Assert.False(string.IsNullOrEmpty(key));
        var session = await SingleSessionAsync();
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(key!)), session.KeyHash);

        // The ticket ends with the session (the cookie stores the time to the second).
        Assert.InRange(session.ExpiresAt - ticket.Properties.ExpiresUtc!.Value, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        Assert.False(ticket.Properties.IsPersistent);
    }

    [SqlServerFact]
    public async Task A_valid_cookie_without_a_server_side_session_is_refused()
    {
        // Correctly encrypted and carrying the Administrator role, but naming no session: the cookie alone is not enough.
        var api = Api();
        using var client = api.CreateAnonymousClient();
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "forged.admin"), new Claim(ClaimTypes.Role, "Administrator")],
            CookieAuthenticationDefaults.AuthenticationScheme,
            ClaimTypes.Name,
            ClaimTypes.Role);
        var forged = CookieFormat(api).Protect(new AuthenticationTicket(
            new ClaimsPrincipal(identity), new AuthenticationProperties { ExpiresUtc = _clock.GetUtcNow().AddHours(1) }, CookieAuthenticationDefaults.AuthenticationScheme));

        using var response = await WithCookie(client, forged).GetAsync(Health);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- CSRF ------------------------------------------------------------------------------------------------

    [SqlServerFact]
    public async Task Every_state_changing_request_needs_the_signed_in_users_csrf_token()
    {
        var api = Api();
        using var client = api.CreateAnonymousClient();
        var anonymousToken = await client.GetCsrfTokenAsync();
        using (var login = await client.PostWithCsrfAsync(LoginEndpointTests.Login, anonymousToken, JsonContent.Create(new { userName = _directory.UserName, password = ControllableDirectory.Password })))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        var userToken = await client.GetCsrfTokenAsync();
        var unknown = new Uri("/api/no-such-endpoint", UriKind.Relative);

        // No token, the token used to sign in (bound to the anonymous visitor), and a token for another user.
        await AssertCsrfRefused(await client.PostWithCsrfAsync(AuthClient.Logout, null));
        await AssertCsrfRefused(await client.PostWithCsrfAsync(AuthClient.Logout, anonymousToken));
        await AssertCsrfRefused(await client.PostWithCsrfAsync(AuthClient.Logout, await OtherUsersTokenAsync()));
        await AssertCsrfRefused(await client.PostWithCsrfAsync(unknown, null));
        using (var put = new HttpRequestMessage(HttpMethod.Put, unknown))
        {
            await AssertCsrfRefused(await client.SendAsync(put));
        }

        using (var allowedThrough = await client.PostWithCsrfAsync(unknown, userToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, allowedThrough.StatusCode);
        }

        using var logout = await client.PostWithCsrfAsync(AuthClient.Logout, userToken);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
    }

    [SqlServerFact]
    public async Task The_sign_in_response_carries_a_csrf_token_for_the_new_user()
    {
        var api = Api();
        using var client = api.CreateAnonymousClient();
        var token = await client.SignInAsync(_directory.UserName, ControllableDirectory.Password);

        using var logout = await client.PostWithCsrfAsync(AuthClient.Logout, token);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
    }

    // --- Sign-out --------------------------------------------------------------------------------------------

    [SqlServerFact]
    public async Task Signing_out_ends_the_session_on_the_server_so_the_old_cookie_is_worthless()
    {
        var api = Api();
        using var client = api.CreateAnonymousClient();
        using var login = await client.PostLoginAsync(new { userName = _directory.UserName, password = ControllableDirectory.Password });
        var cookie = CookieValue(login);
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("csrfToken").GetString();

        using var logout = await client.PostWithCsrfAsync(AuthClient.Logout, token);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var deleted = Assert.Single(logout.Headers.GetValues("Set-Cookie"), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
        Assert.Contains("expires=thu, 01 jan 1970", deleted.ToLowerInvariant(), StringComparison.Ordinal);

        var session = await SingleSessionAsync();
        Assert.Equal(SessionEndReason.SignedOut, session.EndReason);
        Assert.Contains(await AuditAsync(), a => a.Action == AuditAction.SignedOut && a.UserName == _directory.UserName);

        using var replay = api.CreateAnonymousClient();
        using var replayed = await WithCookie(replay, cookie).GetAsync(AuthClient.Me);
        Assert.Equal(HttpStatusCode.Unauthorized, replayed.StatusCode);
    }

    // --- Timeouts --------------------------------------------------------------------------------------------

    [SqlServerFact]
    public async Task A_session_ends_after_twenty_idle_minutes_and_activity_keeps_it_alive()
    {
        var api = Api();
        using var client = api.CreateAnonymousClient();
        await client.SignInAsync(_directory.UserName, ControllableDirectory.Password);

        Assert.Equal(HttpStatusCode.OK, await StatusAfter(client, TimeSpan.FromMinutes(19)));
        Assert.Equal(HttpStatusCode.OK, await StatusAfter(client, TimeSpan.FromMinutes(19)));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAfter(client, TimeSpan.FromMinutes(20)));

        Assert.Equal(SessionEndReason.IdleTimeout, (await SingleSessionAsync()).EndReason);
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAfter(client, TimeSpan.Zero));
    }

    [SqlServerFact]
    public async Task A_session_ends_eight_hours_after_sign_in_however_active()
    {
        var api = Api();
        using var client = api.CreateAnonymousClient();
        await client.SignInAsync(_directory.UserName, ControllableDirectory.Password);

        for (var elapsed = TimeSpan.FromMinutes(15); elapsed < TimeSpan.FromHours(8); elapsed += TimeSpan.FromMinutes(15))
        {
            Assert.Equal(HttpStatusCode.OK, await StatusAfter(client, TimeSpan.FromMinutes(15)));
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAfter(client, TimeSpan.FromMinutes(15)));
    }

    [SqlServerFact]
    public async Task Timeouts_come_from_configuration()
    {
        var api = Api(new Dictionary<string, string?> { ["Session:IdleTimeoutMinutes"] = "5" });
        using var client = api.CreateAnonymousClient();
        await client.SignInAsync(_directory.UserName, ControllableDirectory.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAfter(client, TimeSpan.FromMinutes(5)));
    }

    // --- Periodic directory re-check -------------------------------------------------------------------------

    [SqlServerTheory]
    [InlineData(DirectoryAccessStatus.NotAuthorized)]
    [InlineData(DirectoryAccessStatus.AccountDisabled)]
    [InlineData(DirectoryAccessStatus.AccountExpired)]
    [InlineData(DirectoryAccessStatus.AccountNotFound)]
    public async Task Losing_access_in_the_directory_ends_the_session_at_the_next_check(DirectoryAccessStatus lost)
    {
        var api = Api();
        using var client = api.CreateAnonymousClient();
        await client.SignInAsync(_directory.UserName, ControllableDirectory.Password);
        _directory.Access = lost;

        Assert.Equal(HttpStatusCode.OK, await StatusAfter(client, TimeSpan.FromMinutes(4)));
        Assert.Equal(0, _directory.AccessChecks);
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAfter(client, TimeSpan.FromMinutes(1)));
        Assert.Equal(1, _directory.AccessChecks);

        Assert.Equal(SessionEndReason.AccessRevoked, (await SingleSessionAsync()).EndReason);
        var revoked = Assert.Single(await AuditAsync(), a => a.Action == AuditAction.AccessRevoked);
        Assert.Equal("system", revoked.UserName);
        Assert.Equal(lost.ToString(), JsonDocument.Parse(revoked.NewValues!).RootElement.GetProperty("Reason").GetString());

        // The cookie stays refused.
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAfter(client, TimeSpan.Zero));
    }

    [SqlServerFact]
    public async Task While_the_directory_is_unreachable_sessions_continue_only_for_the_grace_period()
    {
        var api = Api();
        using var client = api.CreateAnonymousClient();
        await client.SignInAsync(_directory.UserName, ControllableDirectory.Password);
        _directory.Access = DirectoryAccessStatus.DirectoryUnavailable;

        // The check is due after 5 minutes; failures are retried at most once a minute.
        Assert.Equal(HttpStatusCode.OK, await StatusAfter(client, TimeSpan.FromMinutes(5)));
        Assert.Equal(1, _directory.AccessChecks);
        Assert.Equal(HttpStatusCode.OK, await StatusAfter(client, TimeSpan.FromSeconds(30)));
        Assert.Equal(1, _directory.AccessChecks);
        Assert.Equal(HttpStatusCode.OK, await StatusAfter(client, TimeSpan.FromSeconds(30)));
        Assert.Equal(2, _directory.AccessChecks);

        // 5 minutes plus the 15-minute grace period after the last confirmation, the session ends.
        Assert.Equal(HttpStatusCode.OK, await StatusAfter(client, TimeSpan.FromMinutes(7)));
        Assert.Equal(HttpStatusCode.OK, await StatusAfter(client, TimeSpan.FromMinutes(6)));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAfter(client, TimeSpan.FromMinutes(1)));
        Assert.Equal(SessionEndReason.DirectoryUnavailable, (await SingleSessionAsync()).EndReason);
    }

    [SqlServerFact]
    public async Task A_session_survives_a_short_directory_outage()
    {
        var api = Api();
        using var client = api.CreateAnonymousClient();
        await client.SignInAsync(_directory.UserName, ControllableDirectory.Password);
        _directory.Access = DirectoryAccessStatus.DirectoryUnavailable;
        Assert.Equal(HttpStatusCode.OK, await StatusAfter(client, TimeSpan.FromMinutes(6)));

        _directory.Access = DirectoryAccessStatus.Allowed;
        Assert.Equal(HttpStatusCode.OK, await StatusAfter(client, TimeSpan.FromMinutes(2)));

        var session = await SingleSessionAsync();
        Assert.True(session.IsActive);
        Assert.Null(session.LastFailedAccessCheckAt);
        Assert.Equal(_clock.GetUtcNow(), session.LastAccessCheckAt);
    }

    [SqlServerFact]
    public async Task A_session_ended_in_the_database_is_refused_at_once()
    {
        var api = Api();
        using var client = api.CreateAnonymousClient();
        await client.SignInAsync(_directory.UserName, ControllableDirectory.Password);
        var session = await SingleSessionAsync();

        // What an administrator runs to revoke a session (see docs/active-directory.md).
        await using (var context = database.CreateContext())
        {
            await context.Database.ExecuteSqlAsync(
                $"UPDATE UserSessions SET EndedAt = SYSDATETIMEOFFSET(), EndReason = 6 WHERE Id = {session.Id} AND EndedAt IS NULL");
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAfter(client, TimeSpan.Zero));
    }

    // --- Data Protection keys --------------------------------------------------------------------------------

    [SqlServerFact]
    public async Task Sessions_survive_a_restart_because_the_cookie_keys_are_kept()
    {
        var keys = Directory.CreateTempSubdirectory("ei-keys-").FullName;
        try
        {
            var settings = new Dictionary<string, string?> { ["DataProtection:KeysDirectory"] = keys };
            string cookie;
            await using (var first = new TestApiFactory(database.ConnectionString, useTestAuthentication: false, settings: settings, configureServices: Services))
            {
                using var client = first.CreateAnonymousClient();
                using var login = await client.PostLoginAsync(new { userName = _directory.UserName, password = ControllableDirectory.Password });
                cookie = CookieValue(login);
            }

            Assert.NotEmpty(Directory.GetFiles(keys, "key-*.xml"));
            await using var restarted = new TestApiFactory(database.ConnectionString, useTestAuthentication: false, settings: settings, configureServices: Services);
            using var again = restarted.CreateAnonymousClient();
            using var response = await WithCookie(again, cookie).GetAsync(AuthClient.Me);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            Directory.Delete(keys, recursive: true);
        }
    }

    private TestApiFactory Api(IReadOnlyDictionary<string, string?>? settings = null)
    {
        var api = new TestApiFactory(database.ConnectionString, useTestAuthentication: false, settings: settings, configureServices: Services);
        _hosts.Add(api);
        return api;
    }

    private void Services(IServiceCollection services)
    {
        services.AddSingleton<TimeProvider>(_clock);
        services.AddScoped<IDirectoryService>(_ => _directory);
    }

    private async Task<HttpStatusCode> StatusAfter(HttpClient client, TimeSpan wait)
    {
        _clock.Advance(wait);
        using var response = await client.GetAsync(AuthClient.Me);
        return response.StatusCode;
    }

    /// <summary>A CSRF token issued, with the same keys, to another signed-in user.</summary>
    private async Task<string> OtherUsersTokenAsync()
    {
        var other = new ControllableDirectory();
        await using var otherApi = new TestApiFactory(database.ConnectionString, useTestAuthentication: false, configureServices: services =>
        {
            services.AddSingleton<TimeProvider>(_clock);
            services.AddScoped<IDirectoryService>(_ => other);
        });
        using var client = otherApi.CreateAnonymousClient();
        return await client.SignInAsync(other.UserName, ControllableDirectory.Password);
    }

    private async Task<UserSession> SingleSessionAsync()
    {
        await using var context = database.CreateContext();
        return await context.UserSessions.AsNoTracking().SingleAsync(s => s.AdminUser.ObjectGuid == _directory.ObjectGuid);
    }

    private async Task<List<AuditLog>> AuditAsync()
    {
        await using var context = database.CreateContext();
        var user = await context.AdminUsers.SingleAsync(u => u.ObjectGuid == _directory.ObjectGuid);
        var id = user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return await context.AuditLogs.Where(a => a.EntityName == "AdminUser" && a.EntityId == id).OrderBy(a => a.Id).ToListAsync();
    }

    private static async Task AssertCsrfRefused(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await ProblemJson.ReadAsync(response);
            Assert.Equal("csrf_invalid", problem.GetProperty("code").GetString());
            Assert.Equal("Güvenlik doğrulaması başarısız oldu.", problem.GetProperty("title").GetString());
        }
    }

    private static string CookieValue(HttpResponseMessage response)
    {
        var header = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
        return header[(SessionCookie.Length + 1)..header.IndexOf(';', StringComparison.Ordinal)];
    }

    private static HttpClient WithCookie(HttpClient client, string cookie)
    {
        client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie}={cookie}");
        return client;
    }

    private static ISecureDataFormat<AuthenticationTicket> CookieFormat(TestApiFactory api) =>
        api.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme).TicketDataFormat;

    private static AuthenticationTicket Ticket(TestApiFactory api, string cookie) =>
        CookieFormat(api).Unprotect(cookie) ?? throw new InvalidOperationException("The cookie could not be decrypted.");

    /// <summary>A directory with one member, whose access each test decides.</summary>
    private sealed class ControllableDirectory : IDirectoryService
    {
        public const string Password = "controllable-password";

        // A user of its own for every test, so tests sharing the database never see each other's sessions.
        public Guid ObjectGuid { get; } = Guid.NewGuid();

        public string UserName => "s" + ObjectGuid.ToString("N")[..12];

        public DirectoryAccessStatus Access { get; set; } = DirectoryAccessStatus.Allowed;

        public int AccessChecks { get; private set; }

        public Task<DirectorySignInResult> SignInAsync(string userName, string password, CancellationToken cancellationToken) =>
            Task.FromResult(userName == UserName && password == Password
                ? DirectorySignInResult.Succeeded(new DirectoryAccount(ObjectGuid, "S-1-5-21-1-2-3-1201", UserName, "Oturum Testi"))
                : DirectorySignInResult.Failed(DirectorySignInStatus.InvalidCredentials));

        public Task<DirectoryAccessStatus> CheckAccessAsync(Guid objectGuid, CancellationToken cancellationToken)
        {
            AccessChecks++;
            return Task.FromResult(objectGuid == ObjectGuid ? Access : DirectoryAccessStatus.AccountNotFound);
        }
    }
}
