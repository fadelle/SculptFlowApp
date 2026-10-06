namespace PlasticSurgery.Entities.Requests.Inbox;

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
    DateTimeOffset? OccurredAt = null,
    /// <summary>customer_message only, set by CustomerMessageHandler/TelegramWebhookProcessor from
    /// ILeadService's WasCreated — drives the NEW_LEAD notification. Always false for any other caller
    /// (e.g. the n8n ingest endpoint, which never creates a lead itself).</summary>
    bool LeadWasNewlyCreated = false
);
