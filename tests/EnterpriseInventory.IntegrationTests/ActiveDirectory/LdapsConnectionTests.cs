using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using EnterpriseInventory.Application.Authentication;
using EnterpriseInventory.Infrastructure.ActiveDirectory;
using Microsoft.Extensions.Logging;

namespace EnterpriseInventory.IntegrationTests.ActiveDirectory;

/// <summary>
/// The real connection path (LDAP library, TLS handshake, certificate check) against loopback servers that
/// present good and bad certificates, so the rules are proven without a domain controller.
/// </summary>
public sealed class LdapsConnectionTests : IDisposable
{
    private readonly string _tempDirectory = Directory.CreateTempSubdirectory("ei-ldaps-").FullName;
    private readonly X509Certificate2 _ca = TestPki.CreateCa("Test AD CA");
    private readonly CapturingLogger<LdapConnectionFactory> _logger = new();

    public void Dispose()
    {
        _ca.Dispose();
        Directory.Delete(_tempDirectory, recursive: true);
    }

    [Fact]
    public async Task Connects_when_the_certificate_is_issued_for_the_server_by_the_pinned_ca()
    {
        using var certificate = TestPki.CreateServerCertificate(_ca, TlsTestServer.HostName);
        await using var server = new TlsTestServer(certificate);

        using var connection = await Factory(server.Port).ConnectAsync(CancellationToken.None);

        Assert.NotNull(connection);
        await server.FirstHandshake.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Refuses_a_certificate_issued_for_another_name()
    {
        using var certificate = TestPki.CreateServerCertificate(_ca, "dc1.envanter.test");
        await using var server = new TlsTestServer(certificate);

        var error = await ConnectFails(Factory(server.Port), DirectoryFailure.CertificateRejected);

        Assert.Contains("not issued for the configured server name", error.Message, StringComparison.Ordinal);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning
            && entry.Message.Contains("CN=dc1.envanter.test", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refuses_a_certificate_from_a_ca_that_is_not_pinned()
    {
        using var otherCa = TestPki.CreateCa("Attacker CA");
        using var certificate = TestPki.CreateServerCertificate(otherCa, TlsTestServer.HostName);
        await using var server = new TlsTestServer(certificate);

        var error = await ConnectFails(Factory(server.Port), DirectoryFailure.CertificateRejected);

        Assert.Contains("does not chain to the configured CA", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_an_expired_certificate()
    {
        using var certificate = TestPki.CreateServerCertificate(
            _ca, TlsTestServer.HostName, DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(-1));
        await using var server = new TlsTestServer(certificate);

        var error = await ConnectFails(Factory(server.Port), DirectoryFailure.CertificateRejected);

        Assert.Contains("NotTimeValid", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_pinned_ca_refuses_a_ca_the_machine_does_not_trust()
    {
        using var certificate = TestPki.CreateServerCertificate(_ca, TlsTestServer.HostName);
        await using var server = new TlsTestServer(certificate);

        var error = await ConnectFails(Factory(server.Port, pinCa: false), DirectoryFailure.CertificateRejected);

        Assert.Contains("chain is not trusted", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_an_unverifiable_revocation_status_when_revocation_is_checked()
    {
        using var certificate = TestPki.CreateServerCertificate(_ca, TlsTestServer.HostName);
        await using var server = new TlsTestServer(certificate);

        var error = await ConnectFails(Factory(server.Port, checkRevocation: true), DirectoryFailure.CertificateRejected);

        Assert.Contains("RevocationStatusUnknown", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_a_port_that_does_not_speak_tls()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var plainServer = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await client.GetStream().WriteAsync("not tls\n"u8.ToArray());
            client.Client.Shutdown(SocketShutdown.Both);
        });

        await ConnectFails(Factory(port), DirectoryFailure.TlsHandshakeFailed, DirectoryFailure.Unreachable);
        await plainServer.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Reports_a_closed_port_as_unreachable()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        await ConnectFails(Factory(port), DirectoryFailure.Unreachable);
    }

    [Fact]
    public async Task Gives_up_on_a_server_that_never_answers_the_handshake()
    {
        await using var server = new TlsTestServer(certificate: null);
        var stopwatch = Stopwatch.StartNew();

        await ConnectFails(Factory(server.Port, connectTimeoutSeconds: 1), DirectoryFailure.Timeout);

        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromSeconds(0.9), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Stops_when_the_caller_cancels()
    {
        await using var server = new TlsTestServer(certificate: null);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Factory(server.Port).ConnectAsync(cancellation.Token));
    }

    [Fact]
    public async Task A_bind_the_server_never_answers_stops_when_the_caller_cancels()
    {
        using var certificate = TestPki.CreateServerCertificate(_ca, TlsTestServer.HostName);
        await using var server = new TlsTestServer(certificate);
        using var connection = await Factory(server.Port, operationTimeoutSeconds: 120).ConnectAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => connection.BindAsync("ayse.admin@envanter.test", "Any-Password-1", cancellation.Token));

        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_sign_in_the_server_never_answers_fails_closed_within_the_sign_in_deadline()
    {
        using var certificate = TestPki.CreateServerCertificate(_ca, TlsTestServer.HostName);
        await using var server = new TlsTestServer(certificate);
        var options = Options(server.Port, connectTimeoutSeconds: 1, operationTimeoutSeconds: 2);
        options.AllowedGroupSid = "S-1-5-21-1-2-3-1105";
        options.NestedGroupPolicy = NestedGroupPolicy.DirectMembershipOnly;
        using var factory = new LdapConnectionFactory(Microsoft.Extensions.Options.Options.Create(options), _logger);
        var service = new LdapDirectoryService(factory, Microsoft.Extensions.Options.Options.Create(options), new CapturingLogger<LdapDirectoryService>());
        var stopwatch = Stopwatch.StartNew();

        var result = await service.SignInAsync("ayse.admin", "Any-Password-1", CancellationToken.None);

        Assert.Equal(DirectorySignInStatus.DirectoryUnavailable, result.Status);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(6));
    }

    // A generous timeout everywhere except in the timeout tests, so a slow machine cannot turn a refusal into a timeout.
    private LdapConnectionFactory Factory(
        int port, bool pinCa = true, bool checkRevocation = false, int connectTimeoutSeconds = 10, int operationTimeoutSeconds = 15) =>
        new(Microsoft.Extensions.Options.Options.Create(Options(port, pinCa, checkRevocation, connectTimeoutSeconds, operationTimeoutSeconds)), _logger);

    private ActiveDirectoryOptions Options(
        int port, bool pinCa = true, bool checkRevocation = false, int connectTimeoutSeconds = 10, int operationTimeoutSeconds = 15) => new()
        {
            Domain = "envanter.test",
            ServerFqdn = TlsTestServer.HostName,
            Port = port,
            BaseDn = "DC=envanter,DC=test",
            TrustedCaCertificatePath = pinCa ? TestPki.WritePem(_ca, _tempDirectory) : null,
            CheckCertificateRevocation = checkRevocation,
            ConnectTimeoutSeconds = connectTimeoutSeconds,
            OperationTimeoutSeconds = operationTimeoutSeconds,
        };

    private static async Task<DirectoryUnavailableException> ConnectFails(LdapConnectionFactory factory, params DirectoryFailure[] expected)
    {
        var error = await Assert.ThrowsAsync<DirectoryUnavailableException>(() => factory.ConnectAsync(CancellationToken.None));
        Assert.Contains(error.Failure, expected);
        return error;
    }
}
