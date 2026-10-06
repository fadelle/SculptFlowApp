using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Entities.Requests.WhatsApp;

/// <summary>
/// Normalized health webhook event n8n forwards after classifying a Meta account_update,
/// account_review_update, phone_number_quality_update, or phone_number_name_update — see
/// POST /api/integrations/whatsapp/health/events and WhatsAppHealthService.ApplyHealthEventAsync.
/// WabaId/PhoneNumberId are used to validate ClinicId against the clinic's actual stored WhatsApp
/// connection rather than trusting ClinicId blindly — at least one of them should be provided.
/// </summary>
public record WhatsAppHealthEventRequest(
    Guid ClinicId,
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
