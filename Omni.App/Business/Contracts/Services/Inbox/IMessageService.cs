using Omni.App.Entities.Dtos.Inbox;
using Omni.App.Entities.Requests.Inbox;
using Omni.App.Entities.Responses.Inbox;

namespace Omni.App.Business.Contracts.Services.Inbox;

/// <summary>
/// Central message orchestration — the one place that writes to the messages table for both
/// directions of traffic:
///   - SendAsync: member replying from the dashboard (Case B). Calls IWhatsAppService to actually
///     send, then records it. n8n's eventual AI-reply path should call this same method (or a
///     thin wrapper around it) instead of duplicating WhatsApp-sending logic.
///   - IngestAsync: everything the backend didn't itself cause — a customer message, a member
///     reply echoed from the WhatsApp Business phone app, an AI message n8n already sent, or a
///     delivery-status update (Cases A, C, and status webhooks).
/// Both paths go through the same idempotency check (external_message_id) and the same SignalR
/// notification calls, so the Inbox behaves identically no matter which case produced a message.
/// </summary>
public interface IMessageService
{
    Task<MessageResponse?> SendAsync(Guid clientId, Guid conversationId, SendMessageRequest request, CancellationToken ct = default);

    /// <summary>Sends an approved WhatsApp template — usable any time (proactive outreach), and the
    /// only option once the service window is closed. Reused by both the Inbox's "Send Template"
    /// action and CampaignService.ProcessRecipientAsync — see that method for the campaign-origin
    /// variant, which sets Origin.Campaign instead of Origin.Dashboard.</summary>
    Task<MessageResponse?> SendTemplateAsync(Guid clientId, Guid conversationId, SendTemplateMessageRequest request, CancellationToken ct = default);

    /// <summary>Campaign-origin variant of SendTemplateAsync — sets Origin.Campaign and links
    /// CampaignId/CampaignRecipientId on the resulting Message instead of Origin.Dashboard. The only
    /// other difference from a normal Inbox template send; everything else (WhatsApp API call,
    /// window rules, SignalR notification) is shared. Used exclusively by CampaignService.</summary>
    Task<MessageResponse?> SendCampaignTemplateAsync(
        Guid clientId, Guid conversationId, Guid whatsAppTemplateId, IReadOnlyList<string> bodyParameters,
        Guid campaignId, Guid campaignRecipientId, CancellationToken ct = default);

    Task<IngestMessageResult> IngestAsync(IngestMessageRequest request, CancellationToken ct = default);

    /// <summary>The AI assistant's own send path (POST /api/conversations/{id}/messages/send with
    /// Sender="ai") — re-checks conversation.mode == ai fresh from the database immediately before
    /// sending (a human may have taken over while the AI was "thinking"), throwing
    /// ConversationNotInAiModeException if not. Unlike SendAsync (members), this never flips the
    /// conversation to human mode — origin=ai, sender_type=ai throughout.</summary>
    Task<MessageResponse?> SendAiReplyAsync(Guid clientId, Guid conversationId, string content, CancellationToken ct = default);

    /// <summary>An automatic text from Omni itself (sender and origin "system"): the opt-out confirmation, Telegram booking
    /// reminders. Same billing and channel rules as a member's text (WhatsApp needs an open service window), but it never
    /// changes the conversation's mode. Null when the conversation isn't the client's.</summary>
    Task<MessageResponse?> SendSystemTextAsync(Guid clientId, Guid conversationId, string content, CancellationToken ct = default);
}
