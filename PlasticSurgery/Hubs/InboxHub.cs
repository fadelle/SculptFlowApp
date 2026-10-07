using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Contracts.Services.Inbox;

namespace PlasticSurgery.Hubs;

/// <summary>
/// Real-time notification channel for the Inbox — see the class doc on InboxNotifier for the
/// "PostgreSQL is authoritative, SignalR just says something changed" principle this follows.
///
/// Group membership: every connection is placed in "clinic:{clinicId}" on connect, where clinicId
/// is resolved server-side via ICurrentClinicContext (clinic_users, tied to the logged-in Identity
/// user's cookie, which flows with the SignalR connection automatically) — never taken from the
/// client. [Authorize] refuses the connection outright if there's no logged-in session at all.
/// </summary>
[Authorize]
public class InboxHub : Hub
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly IConversationService _conversations;

    public InboxHub(ICurrentClinicContext clinicContext, IConversationService conversations)
    {
        _clinicContext = clinicContext;
        _conversations = conversations;
    }

    public override async Task OnConnectedAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync(Context.ConnectionAborted);
        if (clinicId is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, ClinicGroup(clinicId.Value));
        }
        await base.OnConnectedAsync();
    }

    /// <summary>Optional finer-grained subscription for whichever conversation is currently open in
    /// the Inbox. Validates the conversation actually belongs to the caller's (server-resolved)
    /// clinic before joining — never trust a conversationId alone as proof of access.</summary>
    public async Task JoinConversation(Guid conversationId)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync(Context.ConnectionAborted);
        if (clinicId is null) return;

        var belongsToClinic = await _conversations.BelongsToClinicAsync(clinicId.Value, conversationId, Context.ConnectionAborted);
        if (!belongsToClinic) return;

        await Groups.AddToGroupAsync(Context.ConnectionId, ConversationGroup(conversationId));
    }

    public Task LeaveConversation(Guid conversationId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, ConversationGroup(conversationId));

    public static string ClinicGroup(Guid clinicId) => $"clinic:{clinicId}";
    public static string ConversationGroup(Guid conversationId) => $"conversation:{conversationId}";
}
