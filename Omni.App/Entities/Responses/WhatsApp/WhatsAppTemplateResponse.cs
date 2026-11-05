namespace Omni.App.Entities.Responses.WhatsApp;

public record WhatsAppTemplateResponse(
    Guid Id,
    Guid ClientId,
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
    DateTimeOffset? LastMetaEventAt = null,
    // Set only on the answer to a create, edit or submit that couldn't reach Meta (or the WhatsApp connection needs renewing):
    // Meta's explanation. The template stays a draft (QA t8-12). Never stored.
    string? SubmitError = null
);
