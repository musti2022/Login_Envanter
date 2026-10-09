using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace EnterpriseInventory.IntegrationTests.ActiveDirectory;

/// <summary>
/// A loopback server that completes a TLS handshake with the given certificate and then holds the connection,
/// standing in for a domain controller's LDAPS port. Without a certificate it accepts TCP and stays silent.
/// Connect to it as <see cref="HostName"/>: an IP address, so no name resolution can pick another loopback
/// address, and certificates for it carry it as an IP address name.
/// </summary>
internal sealed class TlsTestServer : IAsyncDisposable
{
    public const string HostName = "127.0.0.1";

    private readonly X509Certificate2? _certificate;
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _connections = [];
    private readonly Task _acceptLoop;

    public TlsTestServer(X509Certificate2? certificate)
    {
        _certificate = certificate;
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = AcceptAsync();
    }

    public int Port { get; }

    private readonly TaskCompletionSource _firstHandshake = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the server side of a TLS handshake has finished.</summary>
    public Task FirstHandshake => _firstHandshake.Task;

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Dispose();

        Task[] pending;
        lock (_connections)
        {
            pending = [_acceptLoop, .. _connections];
        }

        await Task.WhenAll(pending).ContinueWith(_ => { }, TaskScheduler.Default);
        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            lock (_connections)
            {
                _connections.Add(ServeAsync(client));
            }
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                if (_certificate is null)
                {
                    await Task.Delay(Timeout.Infinite, _stop.Token);
                    return;
                }

                await using var tls = new SslStream(client.GetStream());
                await tls.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions { ServerCertificate = _certificate }, _stop.Token);
                _firstHandshake.TrySetResult();

                // Hold the connection until the client closes it.
                var buffer = new byte[1024];
                while (await tls.ReadAsync(buffer, _stop.Token) > 0)
                {
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or System.Security.Authentication.AuthenticationException)
            {
            }
        }
    }
}
