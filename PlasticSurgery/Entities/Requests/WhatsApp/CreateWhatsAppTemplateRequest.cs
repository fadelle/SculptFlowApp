namespace PlasticSurgery.Entities.Requests.WhatsApp;

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
