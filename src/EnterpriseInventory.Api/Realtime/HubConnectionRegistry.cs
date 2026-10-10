using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace EnterpriseInventory.Api.Realtime;

/// <summary>
/// The live connections open on this server and the session each belongs to, so they can be closed the moment the
/// session ends: at sign-out here, or when <see cref="HubSessionMonitor"/> finds it ended (also by another server).
/// </summary>
internal sealed class HubConnectionRegistry
{
    private readonly ConcurrentDictionary<string, Connection> _connections = new(StringComparer.Ordinal);

    public int Count => _connections.Count;

    public void Add(string connectionId, string sessionKey, HubCallerContext context) =>
        _connections[connectionId] = new Connection(sessionKey, context);

    public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);

    /// <summary>The sessions that have at least one open connection.</summary>
    public IReadOnlyCollection<string> SessionKeys() =>
        [.. _connections.Values.Select(connection => connection.SessionKey).Distinct(StringComparer.Ordinal)];

    /// <summary>Closes every connection of the session; returns how many there were.</summary>
    public int Close(string sessionKey)
    {
        var closed = 0;
        foreach (var (connectionId, connection) in _connections)
        {
            if (string.Equals(connection.SessionKey, sessionKey, StringComparison.Ordinal))
            {
                connection.Context.Abort();
                _connections.TryRemove(connectionId, out _);
                closed++;
            }
        }

        return closed;
    }

    private sealed record Connection(string SessionKey, HubCallerContext Context);
}
