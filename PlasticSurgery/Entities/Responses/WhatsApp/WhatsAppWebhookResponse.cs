using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Entities.Responses.WhatsApp;

/// <summary>
/// The ONE flat response POST /api/integrations/whatsapp/webhook returns to n8n — see
/// Business/Engines/WhatsApp/MetaWebhookProcessor.cs. Deliberately not an array/wrapper: n8n's entire
/// job after calling this endpoint is checking ShouldRunAi, nothing else. Fields that don't apply
/// to a given EventType are null. When the raw payload contained multiple events, this represents
/// either the most recent AI-eligible customer message (if any), or the last event processed —
/// every contained event is still persisted/broadcast internally regardless of which one is
/// reflected here (see the processor for the selection rule).
/// </summary>
public record WhatsAppWebhookResponse(
    bool Processed,
    string EventType,
    bool ShouldRunAi,
    Guid? ClinicId,
    Guid? WhatsAppConnectionId = null,
    Guid? ConversationId = null,
    Guid? LeadId = null,
    Guid? MessageId = null,
    string? ExternalMessageId = null,
    string? Mode = null,
    string? Status = null,
    Guid? TemplateId = null,
    string? MetaTemplateId = null,
    string? TemplateName = null,
    string? HealthLevel = null,
    string? HealthEventType = null,
    string? Message = null,
    /// <summary>CustomerMessage only — carried so the caller (WhatsAppWebhookController) can build
    /// the normalized AI-trigger payload for n8n straight from this response, with no extra lookup.</summary>
    string? MessageType = null,
    string? Content = null,
    /// <summary>Interactive/button replies only — the stable Meta reply id (e.g. "rhinoplasty"),
    /// separate from Content (the user-visible title sent as messageText). Null otherwise.</summary>
    string? SelectedValue = null
);
