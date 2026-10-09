using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnterpriseInventory.Domain.Auditing;
using EnterpriseInventory.IntegrationTests.ActiveDirectory;
using EnterpriseInventory.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseInventory.IntegrationTests.Api;

/// <summary>
/// Successful sign-ins: the session cookie, access with it, and the administrator and audit records written for
/// each sign-in.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public class SignInRecordingTests(SqlServerDatabaseFixture database)
{
    private static readonly Uri Health = new("/api/health", UriKind.Relative);

    [SqlServerFact]
    public async Task A_member_gets_a_secure_session_cookie_that_grants_access()
    {
        await using var api = new TestApiFactory(database.ConnectionString, useTestAuthentication: false, settings: LoginEndpointTests.FakeDirectory);
        using var client = api.CreateAnonymousClient();
        using (var before = await client.GetAsync(Health))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, before.StatusCode);
        }

        using var login = await client.PostAsJsonAsync(LoginEndpointTests.Login, new { userName = "dev.admin", password = LoginEndpointTests.FakePassword });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var user = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("dev.admin", user.GetProperty("userName").GetString());
        Assert.Equal("Geliştirici Yönetici", user.GetProperty("displayName").GetString());
        Assert.Equal(["Administrator"], user.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));

        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("__Host-EnterpriseInventory=", cookie, StringComparison.Ordinal);
        var attributes = cookie.Split(';', StringSplitOptions.TrimEntries).Skip(1).Select(a => a.ToLowerInvariant()).ToList();
        Assert.Contains("path=/", attributes);
        Assert.Contains("secure", attributes);
        Assert.Contains("httponly", attributes);
        Assert.Contains("samesite=strict", attributes);
        Assert.DoesNotContain(attributes, a => a.StartsWith("expires=", StringComparison.Ordinal) || a.StartsWith("max-age=", StringComparison.Ordinal));
        Assert.DoesNotContain(attributes, a => a.StartsWith("domain=", StringComparison.Ordinal));

        using var after = await client.GetAsync(Health);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [SqlServerFact]
    public async Task Each_sign_in_updates_one_administrator_record_and_is_audited()
    {
        await using var api = new TestApiFactory(database.ConnectionString, useTestAuthentication: false, settings: LoginEndpointTests.FakeDirectory);
        var correlationIds = new List<string>();
        for (var signIn = 0; signIn < 2; signIn++)
        {
            using var client = api.CreateAnonymousClient();
            using var login = await client.PostAsJsonAsync(LoginEndpointTests.Login, new { userName = "DEV.ADMIN", password = LoginEndpointTests.FakePassword });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            correlationIds.Add(login.Headers.GetValues("X-Correlation-ID").Single());
        }

        await using var context = database.CreateContext();
        var administrator = await context.AdminUsers.SingleAsync(u => u.SamAccountName == "dev.admin");
        Assert.Equal("Geliştirici Yönetici", administrator.DisplayName);
        Assert.True(administrator.LastLoginAt >= administrator.FirstLoginAt);

        var audit = await AuditEntries(context, administrator.Id);
        Assert.Equal(correlationIds, audit.Select(a => a.CorrelationId));
        Assert.All(audit, entry =>
        {
            Assert.Equal(AuditAction.SignedIn, entry.Action);
            Assert.Equal("dev.admin", entry.UserName);
            Assert.Null(entry.OldValues);
            Assert.Equal("dev.admin", JsonDocument.Parse(entry.NewValues!).RootElement.GetProperty("SamAccountName").GetString());
        });
    }

    [ActiveDirectoryAndSqlServerFact]
    public async Task A_member_of_the_test_directory_signs_in_with_the_directory_identity()
    {
        await using var api = new TestApiFactory(database.ConnectionString, useTestAuthentication: false, settings: TestActiveDirectory.Settings());
        using var client = api.CreateAnonymousClient();

        using var login = await client.PostAsJsonAsync(LoginEndpointTests.Login, new { userName = "ayse.admin", password = TestActiveDirectory.UserPassword });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var user = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Ayşe Yılmaz", user.GetProperty("displayName").GetString());
        using (var after = await client.GetAsync(Health))
        {
            Assert.NotEqual(HttpStatusCode.Unauthorized, after.StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, after.StatusCode);
        }

        await using var context = database.CreateContext();
        var administrator = await context.AdminUsers.SingleAsync(u => u.SamAccountName == "ayse.admin");
        Assert.NotEqual(Guid.Empty, administrator.ObjectGuid);
        Assert.Contains(await AuditEntries(context, administrator.Id), a => a.Action == AuditAction.SignedIn);
    }

    private static Task<List<AuditLog>> AuditEntries(Infrastructure.Persistence.ApplicationDbContext context, int administratorId) =>
        context.AuditLogs
            .Where(a => a.EntityName == "AdminUser" && a.EntityId == administratorId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .OrderBy(a => a.Id)
            .ToListAsync();
}
