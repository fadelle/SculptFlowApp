namespace PlasticSurgery.Integrations.WhatsApp.Models;

/// <summary>What kind of normalized event MetaWebhookParser extracted — the only thing
/// MetaWebhookProcessor and the Handlers switch on. Never Meta's raw "field" string.</summary>
public enum ParsedMetaEventKind
{
    CustomerMessage,
    BusinessAppEcho,
    MessageStatus,
    TemplateStatus,
    WhatsAppHealth,
    /// <summary>change.field == "history" — Coexistence history sync. Never treated as a live
    /// message and never AI-eligible; see HistoryHandler.</summary>
    HistorySync,
    /// <summary>change.field == "smb_app_state_sync" — WhatsApp Business App/Coexistence state
    /// sync. Never AI-eligible; see AppStateSyncHandler.</summary>
    AppStateSync,
    Unknown
}

/// <summary>
/// One normalized event extracted from a raw Meta WhatsApp webhook payload by MetaWebhookParser.
/// This is the boundary: MetaWebhookProcessor and every Handler work only with this shape — none of
/// them touch entry[].changes[].value... directly. See MetaWebhookParser's own doc comment for how
/// defensively it's built (unrecognized fields degrade to null/Unknown rather than throwing).
/// </summary>
public class ParsedMetaEvent
{
    public required ParsedMetaEventKind Kind { get; init; }

    // Clinic resolution hints — MetaWebhookProcessor resolves the trusted ClinicId from these
    // against the clinic's own stored WhatsApp connection; neither is ever trusted as a clinic
    // identity by itself, and none of this ever comes from n8n.
    public string? PhoneNumberId { get; init; }
    public string? WabaId { get; init; }

    // CustomerMessage / BusinessAppEcho
    public string? CustomerWaId { get; init; }
    public string? CustomerName { get; init; }
    public string? MessageType { get; init; }
    public string? Content { get; init; }
    public string? MetadataJson { get; init; }
    /// <summary>Interactive/button replies only — the stable Meta reply id (e.g. "rhinoplasty"),
    /// separate from Content (the user-visible title, e.g. "Rhinoplasty"). Null for every other
    /// message type. See MetaWebhookParser.ExtractMessageContent.</summary>
    public string? SelectedValue { get; init; }
    public string? ExternalMessageId { get; init; }
    public DateTimeOffset? Timestamp { get; init; }

    // MessageStatus
    public string? DeliveryStatus { get; init; }
    public string? FailureCode { get; init; }
    public string? FailureReason { get; init; }

    // TemplateStatus
    public string? MetaTemplateId { get; init; }
    public string? TemplateName { get; init; }
    public string? TemplateLanguage { get; init; }
    public string? TemplateCategory { get; init; }
    public string? TemplateStatus { get; init; }
    public string? TemplateReason { get; init; }
    public string? TemplateQualityRating { get; init; }
    public string? TemplatePreviousCategory { get; init; }
    public string? TemplateCurrentCategory { get; init; }
    public string? TemplateComponentsJson { get; init; }

    // WhatsAppHealth
    public string? HealthEventType { get; init; }
    public string? HealthStatus { get; init; }
    public string? HealthQualityRating { get; init; }
    public string? HealthCode { get; init; }
    public string? HealthMessage { get; init; }

    // Unknown / diagnostics
    public string? RawFieldName { get; init; }
    public string RawJson { get; init; } = "{}";
}
