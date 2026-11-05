using System.Security.Claims;

namespace Omni.App.Business.Contracts.Managers;

/// <summary>
/// The live Inbox (SignalR) connections of this app instance, by user and client (QA t11-auto-02: a deactivated member's open
/// Inbox kept receiving the client's live updates, because access is only checked on HTTP requests). Closing a connection
/// stops every live update to it at once; the page's next request is then refused as usual.
/// </summary>
public interface ILiveConnections
{
    void Add(string connectionId, string userId, Guid clientId, ClaimsPrincipal user, Action abort);

    void Remove(string connectionId);

    /// <summary>Closes the user's connections (their membership was deactivated or removed). Returns how many.</summary>
    int CloseUser(string userId);

    /// <summary>Closes every connection to the client (the client was deactivated). Returns how many.</summary>
    int CloseClient(Guid clientId);

    /// <summary>The open connections, for the periodic access re-check (LiveConnectionCheckJob).</summary>
    IReadOnlyList<LiveConnection> List();

    /// <summary>Closes one connection (it failed the re-check).</summary>
    bool Close(string connectionId);
}

public sealed record LiveConnection(string ConnectionId, string UserId, Guid ClientId, ClaimsPrincipal User);
