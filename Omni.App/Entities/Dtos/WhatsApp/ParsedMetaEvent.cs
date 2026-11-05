using Omni.App.Common.Enums;

namespace Omni.App.Entities.Dtos.WhatsApp;

/// <summary>
/// One normalized event extracted from a raw Meta WhatsApp webhook payload by MetaWebhookParser.
/// This is the boundary: MetaWebhookProcessor and every Handler work only with this shape — none of
/// them touch entry[].changes[].value... directly. See MetaWebhookParser's own doc comment for how
/// defensively it's built (unrecognized fields degrade to null/Unknown rather than throwing).
/// </summary>
public record ParsedMetaEvent
{
    public required ParsedMetaEventKind Kind { get; init; }

    // Client resolution hints — MetaWebhookProcessor resolves the trusted ClientId from these
    // against the client's own stored WhatsApp connection; neither is ever trusted as a client
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
    /// <summary>Click-to-WhatsApp: the ad the customer came from (Meta's referral object; also kept whole in MetadataJson,
    /// with ctwa_clid). Null for a message that didn't come from an ad (QA t10-08).</summary>
    public MetaAdReferral? Referral { get; init; }
    /// <summary>A system message telling the customer changed number: their new wa_id (QA t10-08).</summary>
    public string? NewWaId { get; init; }

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
    /// <summary>Meta's own explanation and recommendation (rejection_info, or other_info for a pause), when it sends one.</summary>
    public string? TemplateReasonDetail { get; init; }
    /// <summary>template_category_update announcing a change still to come: the category Meta will move it to, and when.</summary>
    public string? TemplatePendingCategory { get; init; }
    public DateTimeOffset? TemplatePendingCategoryAt { get; init; }

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
