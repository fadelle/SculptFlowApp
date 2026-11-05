using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Inbox;

namespace Omni.App.Hubs;

/// <summary>
/// Real-time notification channel for the Inbox — see the class doc on InboxNotifier for the
/// "PostgreSQL is authoritative, SignalR just says something changed" principle this follows.
///
/// Group membership: every connection is placed in "client:{clientId}" on connect, where clientId
/// is resolved server-side via ICurrentClientContext (client_users, tied to the logged-in Identity
/// user's cookie, which flows with the SignalR connection automatically) — never taken from the
/// client. [Authorize] refuses the connection outright if there's no logged-in session at all.
/// </summary>
[Authorize]
public class InboxHub : Hub
{
    private readonly ICurrentClientContext _clientContext;
    private readonly IConversationService _conversations;
    private readonly ILiveConnections _live;

    public InboxHub(ICurrentClientContext clientContext, IConversationService conversations, ILiveConnections live)
    {
        _clientContext = clientContext;
        _conversations = conversations;
        _live = live;
    }

    public override async Task OnConnectedAsync()
    {
        var access = await _clientContext.GetAccessAsync(Context.ConnectionAborted);
        if (access is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, ClientGroup(access.ClientId));
            // Known by user and client, so losing access closes it (QA t11-auto-02; ILiveConnections, LiveConnectionCheckJob).
            var context = Context;
            _live.Add(context.ConnectionId, access.UserId, access.ClientId, context.User ?? new System.Security.Claims.ClaimsPrincipal(), context.Abort);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _live.Remove(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Optional finer-grained subscription for whichever conversation is currently open in
    /// the Inbox. Validates the conversation actually belongs to the caller's (server-resolved)
    /// client before joining — never trust a conversationId alone as proof of access.</summary>
    public async Task JoinConversation(Guid conversationId)
    {
        var clientId = await _clientContext.GetClientIdAsync(Context.ConnectionAborted);
        if (clientId is null) return;

        var belongsToClient = await _conversations.BelongsToClientAsync(clientId.Value, conversationId, Context.ConnectionAborted);
        if (!belongsToClient) return;

        await Groups.AddToGroupAsync(Context.ConnectionId, ConversationGroup(conversationId));
    }

    public Task LeaveConversation(Guid conversationId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, ConversationGroup(conversationId));

    public static string ClientGroup(Guid clientId) => $"client:{clientId}";
    public static string ConversationGroup(Guid conversationId) => $"conversation:{conversationId}";
}
