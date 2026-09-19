using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public interface IConversationService
{
    Task<ConversationResponse> CreateAsync(CreateConversationRequest request, CancellationToken ct = default);

    /// <summary>Returns the lead's existing conversation on this channel, or creates one if none
    /// exists yet. Used by CampaignService so every campaign recipient lands in the lead's normal
    /// Conversation — a Campaign never creates a separate "campaign conversation".</summary>
    Task<ConversationResponse> GetOrCreateForLeadAsync(Guid clinicId, Guid leadId, string channel, CancellationToken ct = default);

    /// <summary>Same as the overload above, for channels whose conversations are identified by an
    /// external thread id (Telegram: chat.id). Finds the (clinic, lead, channel) conversation — or, for
    /// a lead with none, creates it stamped with <paramref name="externalThreadId"/>. Race-safe via the
    /// unique index on (clinic_id, channel, external_thread_id).</summary>
    Task<ConversationResponse> GetOrCreateForLeadAsync(Guid clinicId, Guid leadId, string channel, string externalThreadId, CancellationToken ct = default);

    Task<(ConversationResponse Conversation, IReadOnlyList<MessageResponse> Messages)?> GetByIdWithMessagesAsync(
        Guid clinicId, Guid id, CancellationToken ct = default);

    Task<MessageResponse?> AddMessageAsync(Guid clinicId, Guid conversationId, CreateMessageRequest request, CancellationToken ct = default);

    /// <summary>Inbox conversation list (left pane) — newest activity first.</summary>
    Task<(IReadOnlyList<ConversationListRow> Items, int TotalCount)> ListAsync(
        Guid clinicId, int skip, int take, CancellationToken ct = default);

    Task<IReadOnlyList<MessageResponse>> GetMessagesAsync(Guid clinicId, Guid conversationId, CancellationToken ct = default);

    /// <summary>Staff explicitly takes over — AI stops auto-replying.</summary>
    Task<ConversationResponse?> TakeOverAsync(Guid clinicId, Guid conversationId, CancellationToken ct = default);

    /// <summary>AI-agent-initiated handoff (medical question, upset customer, AI uncertain, etc.) —
    /// same effect as TakeOverAsync (flips to human mode) but attributed to the AI as the source,
    /// with an optional reason recorded on the event, so staff can see why the AI backed off. Used
    /// by the AI agent's handoff_to_human tool — see Controllers/AiController.cs.</summary>
    Task<ConversationResponse?> HandoffToHumanAsync(Guid clinicId, Guid conversationId, string? reason, CancellationToken ct = default);

    /// <summary>Staff hands the conversation back — AI may auto-reply again.</summary>
    Task<ConversationResponse?> ReturnToAiAsync(Guid clinicId, Guid conversationId, CancellationToken ct = default);

    Task<ConversationResponse?> CloseAsync(Guid clinicId, Guid conversationId, CancellationToken ct = default);
}
