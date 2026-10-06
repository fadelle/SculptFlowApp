namespace PlasticSurgery.Entities.Responses.WhatsApp;

/// <summary>Current WhatsApp connection/account health for a clinic — what /WhatsApp/Health and the
/// dashboard's health indicator render. Deliberately simplified (HealthLevel) rather than exposing
/// Meta's raw status vocabulary — see ChannelIntegration.HealthLevel / WhatsAppHealthService.</summary>
public record WhatsAppHealthResponse(
    Guid ConnectionId,
    Guid ClinicId,
    bool Connected,
    string HealthLevel,
    string? DisplayPhoneNumber,
    string? VerifiedName,
    string? WabaId,
    string? AccountStatus,
    string? AccountReviewStatus,
    string? PhoneQualityRating,
    string? PhoneStatus,
    string? NameStatus,
    string? LastProblemCode,
    string? LastProblemMessage,
    DateTimeOffset? LastWebhookAt,
    DateTimeOffset? LastHealthEventAt,
    DateTimeOffset UpdatedAt
);
