namespace PlasticSurgery.Entities.Dtos.WhatsApp;

/// <summary>A provider's current view of a submitted template, same vocabulary as above.</summary>
public record WhatsAppTemplateStatusResult(string Status, string? RejectedReason);
