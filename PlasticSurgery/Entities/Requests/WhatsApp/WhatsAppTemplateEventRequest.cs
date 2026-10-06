namespace PlasticSurgery.Entities.Requests.WhatsApp;

/// <summary>
/// Normalized template webhook event n8n forwards after classifying a Meta template_status_update
/// (or template category/quality change) — see POST /api/integrations/whatsapp/templates/events and
/// WhatsAppTemplateService.ApplyMetaEventAsync. Upserted by (ClinicId, MetaTemplateId) when
/// available, falling back to (ClinicId, Name, Language) for events that don't carry Meta's id.
/// WabaId, when provided, is cross-checked against the clinic's stored WhatsApp connection rather
/// than trusting ClinicId alone — see the service for details.
/// </summary>
public record WhatsAppTemplateEventRequest(
    Guid ClinicId,
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
    DateTimeOffset? OccurredAt
);
