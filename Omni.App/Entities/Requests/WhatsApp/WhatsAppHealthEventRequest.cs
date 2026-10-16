using Omni.App.Entities.Models;

namespace Omni.App.Entities.Requests.WhatsApp;

/// <summary>
/// Normalized health webhook event n8n forwards after classifying a Meta account_update,
/// account_review_update, phone_number_quality_update, or phone_number_name_update — see
/// POST /api/integrations/whatsapp/health/events and WhatsAppHealthService.ApplyHealthEventAsync.
/// WabaId/PhoneNumberId are used to validate ClientId against the client's actual stored WhatsApp
/// connection rather than trusting ClientId blindly — at least one of them should be provided.
/// </summary>
public record WhatsAppHealthEventRequest(
    Guid ClientId,
    string EventType,
    string? WabaId,
    string? PhoneNumberId,
    string? Status,
    string? QualityRating,
    string? Code,
    string? Message,
    DateTimeOffset? OccurredAt,
    string? RawMetadataJson
);
