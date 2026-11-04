namespace Omni.App.Entities.Requests.WhatsApp;

/// <summary>A template's new content (PUT /api/whatsapp/templates/{id}). The name and language can't change: they are
/// how Meta knows the template.</summary>
public record UpdateWhatsAppTemplateRequest(
    string Category,
    string Body,
    string? HeaderType,
    string? HeaderContent,
    string? Footer
);
