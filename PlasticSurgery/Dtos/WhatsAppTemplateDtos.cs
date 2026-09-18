namespace PlasticSurgery.Dtos;

public record WhatsAppTemplateResponse(
    Guid Id,
    Guid ClinicId,
    string? MetaTemplateId,
    string Name,
    string Category,
    string Language,
    string Status,
    string? HeaderType,
    string? HeaderContent,
    string Body,
    string? Footer,
    string? ButtonsJson,
    string? VariablesJson,
    string? RejectionReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? ChannelIntegrationId = null,
    string? QualityRating = null,
    string? PreviousCategory = null,
    string? CurrentCategory = null,
    string? ComponentsJson = null,
    DateTimeOffset? LastMetaEventAt = null
);

/// <summary>
/// Creates a template locally as a draft and submits it to Meta for approval in the same call
/// (WhatsAppTemplateService.CreateAsync) — Meta's own template name rules apply (lowercase,
/// underscores, no spaces); the service doesn't reformat Name for you.
/// </summary>
public record CreateWhatsAppTemplateRequest(
    Guid ClinicId,
    string Name,
    string Category,
    string Language,
    string Body,
    string? HeaderType,
    string? HeaderContent,
    string? Footer,
    string? ButtonsJson,
    string? VariablesJson
);

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
