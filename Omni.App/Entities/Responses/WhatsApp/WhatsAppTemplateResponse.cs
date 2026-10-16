namespace Omni.App.Entities.Responses.WhatsApp;

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
