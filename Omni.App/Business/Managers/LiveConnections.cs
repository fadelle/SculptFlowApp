using System.Collections.Concurrent;
using System.Security.Claims;
using Omni.App.Business.Contracts.Managers;

namespace Omni.App.Business.Managers;

/// <summary>In-memory ILiveConnections (a singleton; the app runs as one instance with no SignalR backplane). Closing calls the
/// connection's own abort (HubCallerContext.Abort), which disconnects it and drops it from its groups.</summary>
public sealed class LiveConnections : ILiveConnections
{
    private sealed record Entry(LiveConnection Connection, Action Abort);

    private readonly ConcurrentDictionary<string, Entry> _connections = new(StringComparer.Ordinal);

    public void Add(string connectionId, string userId, Guid clientId, ClaimsPrincipal user, Action abort) =>
        _connections[connectionId] = new Entry(new LiveConnection(connectionId, userId, clientId, user), abort);

    public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);

    public int CloseUser(string userId) => CloseWhere(c => c.UserId == userId);

    public int CloseClient(Guid clientId) => CloseWhere(c => c.ClientId == clientId);

    public IReadOnlyList<LiveConnection> List() => _connections.Values.Select(e => e.Connection).ToList();

    public bool Close(string connectionId)
    {
        if (!_connections.TryRemove(connectionId, out var entry)) return false;
        entry.Abort();
        return true;
    }

    private int CloseWhere(Func<LiveConnection, bool> match) =>
        _connections.Values.Where(e => match(e.Connection)).Select(e => e.Connection.ConnectionId).ToList().Count(Close);
}
