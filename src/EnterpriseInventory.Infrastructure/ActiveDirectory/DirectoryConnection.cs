using System.Text;
using Novell.Directory.Ldap;

namespace EnterpriseInventory.Infrastructure.ActiveDirectory;

/// <summary>
/// An LDAPS connection whose certificate has been checked, reduced to the operations the application needs.
/// Searches never follow referrals. <see cref="IDirectoryConnection.SearchAsync"/> returns at most two entries, for
/// lookups that expect one; <see cref="IDirectoryConnection.SearchManyAsync"/> returns lists up to a given size.
/// Cancelling an operation's token closes the connection, so nothing waits on a domain controller that stopped
/// answering.
/// </summary>
internal interface IDirectoryConnection : IDisposable
{
    /// <summary>A simple bind inside the verified TLS connection.</summary>
    /// <exception cref="LdapException">Result 49 when the directory refuses the credentials.</exception>
    Task BindAsync(string name, string password, CancellationToken cancellationToken);

    /// <summary>
    /// The identity the directory bound the connection to (RFC 4532 "Who am I?"), e.g. <c>u:CORP\name</c> or
    /// <c>dn:CN=…</c>; <c>null</c> when the directory does not say.
    /// </summary>
    Task<string?> WhoAmIAsync(CancellationToken cancellationToken);

    Task<DirectorySearchResult> SearchAsync(
        string searchBase, int scope, string filter, IReadOnlyCollection<string> attributes, CancellationToken cancellationToken);

    /// <summary>
    /// A subtree search for a list of entries, such as people matching a name: at most <paramref name="maxResults"/>
    /// of them, with <see cref="DirectorySearchResult.Truncated"/> set when the directory had more.
    /// </summary>
    Task<DirectorySearchResult> SearchManyAsync(
        string searchBase, string filter, IReadOnlyCollection<string> attributes, int maxResults, CancellationToken cancellationToken);
}

/// <param name="SkippedReferrals">Continuation references returned instead of entries; they are never followed.</param>
/// <param name="Truncated">The directory stopped at the size limit: more entries matched than were returned.</param>
internal sealed record DirectorySearchResult(IReadOnlyList<LdapEntry> Entries, int SkippedReferrals, bool Truncated = false);

internal sealed class LdapDirectoryConnection : IDirectoryConnection
{
    private const int MaxSearchResults = 2;

    private readonly LdapConnection _connection;
    private readonly IDisposable? _tlsStream;
    private readonly int _operationTimeoutSeconds;
    private int _disposed;

    public LdapDirectoryConnection(LdapConnection connection, IDisposable? tlsStream, int operationTimeoutSeconds)
    {
        _connection = connection;
        _tlsStream = tlsStream;
        _operationTimeoutSeconds = operationTimeoutSeconds;
    }

    public Task BindAsync(string name, string password, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            await _connection.BindAsync(name, password, cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken);

    public Task<string?> WhoAmIAsync(CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            var response = await _connection.ExtendedOperationAsync(new LdapWhoAmIOperation(), cancellationToken).ConfigureAwait(false);
            if (response.ResultCode != LdapException.Success)
            {
                throw new LdapException("Who am I? failed", response.ResultCode, response.ErrorMessage);
            }

            return response.Value is { Length: > 0 } value ? Encoding.UTF8.GetString(value) : null;
        }, cancellationToken);

    public Task<DirectorySearchResult> SearchAsync(
        string searchBase, int scope, string filter, IReadOnlyCollection<string> attributes, CancellationToken cancellationToken) =>
        SearchAsync(searchBase, scope, filter, attributes, MaxSearchResults, cancellationToken);

    public async Task<DirectorySearchResult> SearchManyAsync(
        string searchBase, string filter, IReadOnlyCollection<string> attributes, int maxResults, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxResults, 1);

        // One more than wanted: if it comes, there are more. Not every directory reports its size limit.
        var result = await SearchAsync(searchBase, LdapConnection.ScopeSub, filter, attributes, maxResults + 1, cancellationToken).ConfigureAwait(false);
        return result.Entries.Count > maxResults
            ? result with { Entries = [.. result.Entries.Take(maxResults)], Truncated = true }
            : result;
    }

    private Task<DirectorySearchResult> SearchAsync(
        string searchBase, int scope, string filter, IReadOnlyCollection<string> attributes, int maxResults, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            var constraints = new LdapSearchConstraints
            {
                MaxResults = maxResults,
                ServerTimeLimit = _operationTimeoutSeconds,
                TimeLimit = (int)TimeSpan.FromSeconds(_operationTimeoutSeconds).TotalMilliseconds,
                ReferralFollowing = false,
            };

            var results = await _connection.SearchAsync(searchBase, scope, filter, [.. attributes], false, constraints, cancellationToken)
                .ConfigureAwait(false);
            var entries = new List<LdapEntry>();
            var skippedReferrals = 0;
            var truncated = false;
            while (entries.Count < maxResults && await results.HasMoreAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    entries.Add(await results.NextAsync(cancellationToken).ConfigureAwait(false));
                }
                catch (LdapReferralException)
                {
                    // AD refers subtree searches at the domain root to its DNS and configuration partitions.
                    skippedReferrals++;
                }
                catch (LdapException ex) when (ex.ResultCode == LdapException.SizeLimitExceeded)
                {
                    // The entries before the limit are complete; the directory just had more.
                    truncated = true;
                    break;
                }
            }

            return new DirectorySearchResult(entries, skippedReferrals, truncated);
        }, cancellationToken);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Close(_connection, _tlsStream);
        }
    }

    // LdapConnection.Dispose only closes connections that completed the handshake. The TLS stream owns the socket, so
    // disposing it also closes connections refused during the handshake (an untrusted certificate, for example). A
    // socket that failed before the handshake started is dropped by the library and closed by its finalizer.
    internal static void Close(LdapConnection connection, IDisposable? tlsStream)
    {
        tlsStream?.Dispose();
        connection.Dispose();
    }

    // The library stops waiting for the server's answer only when its own time limit expires, not when the token is
    // cancelled. Closing the connection ends the wait at once; the operation then fails with a connection error,
    // which is reported as the cancellation it really is.
    private async Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var abort = cancellationToken.Register(static state => ((LdapDirectoryConnection)state!).Dispose(), this);
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("The directory operation was cancelled.", ex, cancellationToken);
        }
    }
}
