using Omni.App.Entities.Dtos.Inbox;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Inbox;
using Omni.App.Entities.Responses.Inbox;

namespace Omni.App.Business.Contracts.Services.Inbox;

public interface IConversationService
{
    Task<ConversationResponse> CreateAsync(CreateConversationRequest request, CancellationToken ct = default);

    /// <summary>Returns the lead's existing conversation on this channel, or creates one if none
    /// exists yet. Used by CampaignService so every campaign recipient lands in the lead's normal
    /// Conversation — a Campaign never creates a separate "campaign conversation".</summary>
    Task<ConversationResponse> GetOrCreateForLeadAsync(Guid clientId, Guid leadId, string channel, CancellationToken ct = default);

    /// <summary>Same as the overload above, for channels whose conversations are identified by an
    /// external thread id (Telegram: chat.id). Finds the (client, lead, channel) conversation — or, for
    /// a lead with none, creates it stamped with <paramref name="externalThreadId"/>. Race-safe via the
    /// unique index on (client_id, channel, external_thread_id).</summary>
    Task<ConversationResponse> GetOrCreateForLeadAsync(Guid clientId, Guid leadId, string channel, string externalThreadId, CancellationToken ct = default);

    Task<(ConversationResponse Conversation, IReadOnlyList<MessageResponse> Messages)?> GetByIdWithMessagesAsync(
        Guid clientId, Guid id, CancellationToken ct = default);

    Task<MessageResponse?> AddMessageAsync(Guid clientId, Guid conversationId, CreateMessageRequest request, CancellationToken ct = default);

    /// <summary>Inbox conversation list (left pane) — newest activity first.</summary>
    Task<(IReadOnlyList<ConversationListRow> Items, int TotalCount)> ListAsync(
        Guid clientId, int skip, int take, CancellationToken ct = default);

    Task<IReadOnlyList<MessageResponse>> GetMessagesAsync(Guid clientId, Guid conversationId, CancellationToken ct = default);

    /// <summary>Marks the conversation as read by staff now (clears its unread count). False if it
    /// isn't in this client. Notifies the client's Inbox clients so other open Inboxes update.</summary>
    Task<bool> MarkReadAsync(Guid clientId, Guid conversationId, CancellationToken ct = default);

    /// <summary>Staff explicitly takes over — AI stops auto-replying.</summary>
    Task<ConversationResponse?> TakeOverAsync(Guid clientId, Guid conversationId, CancellationToken ct = default);

    /// <summary>AI-agent-initiated handoff (medical question, upset customer, AI uncertain, etc.) —
    /// same effect as TakeOverAsync (flips to human mode) but attributed to the AI as the source,
    /// with an optional reason recorded on the event, so staff can see why the AI backed off. Used
    /// by the AI agent's handoff_to_human tool — see Controllers/Integrations/AiController.cs.</summary>
    Task<ConversationResponse?> HandoffToHumanAsync(Guid clientId, Guid conversationId, string? reason, CancellationToken ct = default);

    /// <summary>Staff hands the conversation back — AI may auto-reply again. Throws EntitlementDeniedException (422) when
    /// the client's plan doesn't include the AI agent.</summary>
    Task<ConversationResponse?> ReturnToAiAsync(Guid clientId, Guid conversationId, CancellationToken ct = default);

    Task<ConversationResponse?> CloseAsync(Guid clientId, Guid conversationId, CancellationToken ct = default);

    /// <summary>Staff brings a closed conversation back into the working list. A conversation that is already
    /// active is returned unchanged (no event, no broadcast); null if it isn't in this client. A patient's next
    /// message does the same automatically — see MessageService.IngestAsync.</summary>
    Task<ConversationResponse?> ReopenAsync(Guid clientId, Guid conversationId, CancellationToken ct = default);

    /// <summary>Does the conversation belong to this client? Used before letting a caller subscribe to it.</summary>
    Task<bool> BelongsToClientAsync(Guid clientId, Guid conversationId, CancellationToken ct = default);
}
