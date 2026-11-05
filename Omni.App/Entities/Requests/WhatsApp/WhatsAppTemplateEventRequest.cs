namespace Omni.App.Entities.Requests.WhatsApp;

/// <summary>
/// Normalized template webhook event n8n forwards after classifying a Meta template_status_update
/// (or template category/quality change) — see POST /api/integrations/whatsapp/templates/events and
/// WhatsAppTemplateService.ApplyMetaEventAsync. Upserted by (ClientId, MetaTemplateId) when
/// available, falling back to (ClientId, Name, Language) for events that don't carry Meta's id.
/// WabaId, when provided, is cross-checked against the client's stored WhatsApp connection rather
/// than trusting ClientId alone — see the service for details.
/// </summary>
public record WhatsAppTemplateEventRequest(
    Guid ClientId,
    string EventType,
    string? WabaId,
    string? MetaTemplateId,
    string Name,
    string? Language,
    string? Category,
    string? Status,
    string? Reason,
    string? QualityRating,
    string? PreviousCategory,
    string? CurrentCategory,
    string? ComponentsJson,
    DateTimeOffset? OccurredAt,
    // Meta's own explanation and recommendation for a rejection or pause (rejection_info / other_info; QA t10-06).
    string? ReasonDetail = null,
    // template_category_update announcing a change still to come: the new category and when it applies.
    string? PendingCategory = null,
    DateTimeOffset? PendingCategoryAt = null
);
