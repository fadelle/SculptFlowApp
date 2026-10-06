namespace PlasticSurgery.Entities.Dtos.WhatsApp;

/// <summary>A provider's answer to a template submit. Status is in WhatsApp's own vocabulary (PENDING,
/// APPROVED, REJECTED, PAUSED, ...) — WhatsAppTemplateService maps it to our lowercase constants.</summary>
public record WhatsAppTemplateSubmitResult(string ProviderTemplateId, string Status);
