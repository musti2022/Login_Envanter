using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Novell.Directory.Ldap;

namespace EnterpriseInventory.Infrastructure.ActiveDirectory;

internal interface ILdapConnectionFactory
{
    /// <summary>
    /// Opens an LDAPS connection to the configured domain controller once its certificate has been checked by
    /// <see cref="LdapsCertificateValidator"/>. Nothing is sent before the TLS handshake succeeds.
    /// </summary>
    /// <exception cref="DirectoryUnavailableException">The server is unreachable, too slow or not trusted.</exception>
    Task<ILdapConnection> ConnectAsync(CancellationToken cancellationToken);
}

internal sealed partial class LdapConnectionFactory : ILdapConnectionFactory, IDisposable
{
    private static readonly TimeSpan LibraryTimeoutMargin = TimeSpan.FromSeconds(2);

    private readonly ActiveDirectoryOptions _options;
    private readonly ILogger<LdapConnectionFactory> _logger;
    private readonly X509Certificate2? _trustedRoot;

    public LdapConnectionFactory(IOptions<ActiveDirectoryOptions> options, ILogger<LdapConnectionFactory> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger;
        _trustedRoot = string.IsNullOrWhiteSpace(_options.TrustedCaCertificatePath)
            ? null
            : X509CertificateLoader.LoadCertificateFromFile(_options.TrustedCaCertificatePath);
    }

    public async Task<ILdapConnection> ConnectAsync(CancellationToken cancellationToken)
    {
        if (!_options.UseLdaps)
        {
            // The options validator already refuses this; never fall back to plain LDAP if it was bypassed.
            throw new InvalidOperationException("Only LDAPS connections are supported.");
        }

        var server = $"{_options.ServerFqdn}:{_options.Port}";
        var validator = new LdapsCertificateValidator(_trustedRoot, _options.CheckCertificateRevocation);
        CertificateValidationResult? certificateResult = null;
        IDisposable? tlsStream = null;

        var connectionOptions = new LdapConnectionOptions()
            .UseSsl()
            .ConfigureSslProtocols(SslProtocols.Tls12 | SslProtocols.Tls13)
            .ConfigureRemoteCertificateValidationCallback((sender, certificate, chain, errors) =>
            {
                tlsStream = sender as IDisposable;
                certificateResult = validator.Validate(certificate, chain, errors);
                if (!certificateResult.IsTrusted)
                {
                    LogCertificateRejected(server, certificateResult.RejectionReason, certificate?.Subject, certificate?.Issuer);
                }

                return certificateResult.IsTrusted;
            });
        if (_options.CheckCertificateRevocation)
        {
            connectionOptions.CheckCertificateRevocation();
        }

        var connectTimeout = TimeSpan.FromSeconds(_options.ConnectTimeoutSeconds);
        var operationTimeout = (int)TimeSpan.FromSeconds(_options.OperationTimeoutSeconds).TotalMilliseconds;
        var connection = new LdapConnection(connectionOptions)
        {
            // The library's own timeout abandons the pending socket operation instead of cancelling it, so it is only
            // a backstop: our token, which the library passes to DNS, connect and the TLS handshake, fires first.
            ConnectionTimeout = (int)(connectTimeout + LibraryTimeoutMargin).TotalMilliseconds,
            Constraints = new LdapConstraints(operationTimeout, false, null, 0),
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(connectTimeout);
        try
        {
            await connection.ConnectAsync(_options.ServerFqdn, _options.Port, timeout.Token).ConfigureAwait(false);
            return connection;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Close(connection, tlsStream);
            throw Classify(ex, server, certificateResult);
        }
        catch
        {
            Close(connection, tlsStream);
            throw;
        }
    }

    public void Dispose() => _trustedRoot?.Dispose();

    // LdapConnection.Dispose only closes connections that completed the handshake. The TLS stream owns the socket, so
    // disposing it closes connections refused during the handshake (an untrusted certificate, for example). A socket
    // that failed before the handshake started is dropped by the library and closed by its finalizer.
    private static void Close(LdapConnection connection, IDisposable? tlsStream)
    {
        tlsStream?.Dispose();
        connection.Dispose();
    }

    private static DirectoryUnavailableException Classify(Exception ex, string server, CertificateValidationResult? certificateResult)
    {
        if (certificateResult is { IsTrusted: false })
        {
            return new DirectoryUnavailableException(
                DirectoryFailure.CertificateRejected,
                $"The LDAPS certificate of {server} was rejected: {certificateResult.RejectionReason}.",
                ex);
        }

        if (IsTimeout(ex))
        {
            return new DirectoryUnavailableException(
                DirectoryFailure.Timeout, $"The domain controller {server} did not complete the LDAPS handshake in time.", ex);
        }

        return ex switch
        {
            AuthenticationException => new DirectoryUnavailableException(
                DirectoryFailure.TlsHandshakeFailed, $"The TLS handshake with {server} failed; is it serving LDAPS on this port?", ex),
            LdapException { ResultCode: LdapException.ConnectError } or SocketException or IOException or ArgumentException =>
                new DirectoryUnavailableException(DirectoryFailure.Unreachable, $"The domain controller {server} cannot be reached.", ex),
            _ => new DirectoryUnavailableException(DirectoryFailure.Unknown, $"Connecting to {server} failed.", ex),
        };
    }

    // Our own deadline cancels; the LDAP library's connect timeout surfaces as SocketException 258 (WAIT_TIMEOUT)
    // inside an LdapException.
    private static bool IsTimeout(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException or TimeoutException
                || current is SocketException { ErrorCode: 258 } or SocketException { SocketErrorCode: SocketError.TimedOut })
            {
                return true;
            }
        }

        return false;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "LDAPS certificate of {Server} was rejected: {Reason}. Subject: {Subject}; issuer: {Issuer}")]
    private partial void LogCertificateRejected(string server, string? reason, string? subject, string? issuer);
}
