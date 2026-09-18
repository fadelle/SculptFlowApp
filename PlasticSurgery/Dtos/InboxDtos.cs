namespace PlasticSurgery.Dtos;

/// <summary>One row in the Inbox's conversation list (left pane).</summary>
public record ConversationListRow(
    Guid Id,
    Guid LeadId,
    string? LeadFullName,
    string? LeadPhone,
    string? ProcedureName,
    string Channel,
    string Status,
    string Mode,
    string? LastMessagePreview,
    string? LastMessageDirection,
    DateTimeOffset? LastMessageAt,
    DateTimeOffset CreatedAt
);

/// <summary>Body for POST /api/conversations/{id}/messages/send. Two callers share this one route:
/// staff from the Inbox (Sender omitted/null — unchanged behavior, flips the conversation to human
/// mode), and the AI agent via n8n (Sender = "ai" — requires the trusted X-Ingest-Key header,
/// re-checks conversation.mode == ai immediately before sending, and never flips mode). See
/// ConversationsController.SendMessage / MessageService.SendAiReplyAsync.</summary>
public record SendMessageRequest(string Content, string? MessageType = null, string? Sender = null);

/// <summary>Body for POST /api/conversations/{id}/messages/send-template — a staff-initiated
/// template send from the Inbox (Origin stays Dashboard; MessageType becomes "template"). Used
/// when the service window is closed and free-form sending isn't allowed, but also usable any
/// time staff wants to proactively reach out (reminders, follow-ups, etc.) with an approved
/// template. BodyParameters are positional {{1}}, {{2}}... substitutions, in order.</summary>
public record SendTemplateMessageRequest(Guid WhatsAppTemplateId, IReadOnlyList<string>? BodyParameters);

/// <summary>
/// Body for POST /api/messages/ingest — the one trusted entry point n8n uses to record everything
/// that happens on a WhatsApp thread that our own backend didn't directly cause: a customer
/// message, a staff reply echoed from the WhatsApp Business phone app, an AI reply n8n already
/// sent itself, or a delivery-status update. See IngestEventType for the eventType values and
/// MessageService.IngestAsync for how each one is mapped.
/// </summary>
public record IngestMessageRequest(
    Guid ClinicId,
    Guid ConversationId,
    Guid LeadId,
    string EventType,
    string Channel,
    string? Content,
    string? ExternalMessageId,
    string? DeliveryStatus,
    DateTimeOffset? SentAt,
    DateTimeOffset? ReceivedAt,
    /// <summary>"text" (default), "interactive", "image", "document", "audio", "video",
    /// "location", "contact", "reaction" — drives both Message.MessageType and AI eligibility
    /// (only text/interactive customer messages are ever AI-eligible — see IngestMessageResult).</summary>
    string? MessageType = null,
    /// <summary>Raw JSON for content Content alone can't represent — media id/mime/caption, an
    /// interactive reply's button id, a location's lat/lng, etc. Null for plain text.</summary>
    string? MetadataJson = null,
    /// <summary>status_update only, when DeliveryStatus is "failed" — Meta's error code/message.</summary>
    string? FailureCode = null,
    string? FailureReason = null,
    /// <summary>status_update only — Meta's own event timestamp, used for the per-status *_At
    /// column instead of our own ingestion time when provided.</summary>
    DateTimeOffset? OccurredAt = null
);

public static class IngestEventType
{
    public const string CustomerMessage = "customer_message";
    public const string BusinessAppEcho = "business_app_echo";
    public const string AiMessage = "ai_message";
    public const string StatusUpdate = "status_update";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        CustomerMessage, BusinessAppEcho, AiMessage, StatusUpdate
    };
}

/// <summary>Result of an ingest call — Message is null for a status_update (nothing to return) or
/// when the referenced message for a status_update couldn't be found (Found = false).
///
/// AiEligible is the concrete answer to "should n8n also call the AI agent for this?" — computed
/// here, not left for n8n to (re-)derive, per the "n8n holds no business state" principle. True
/// only for a customer_message/business-originated interactive reply, in a conversation currently
/// in AI mode, with a message type the AI is allowed to see (text/interactive) — never for media,
/// location, contact, reactions, echoes, AI's own messages, or status updates.</summary>
public record IngestMessageResult(bool Found, bool Deduplicated, MessageResponse? Message, string? ConversationMode = null, bool AiEligible = false);
