namespace PlasticSurgery.Dtos;

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

/// <summary>One row of WhatsApp health history for /WhatsApp/Health's event log.</summary>
public record WhatsAppHealthEventResponse(
    Guid Id,
    string EventType,
    string? Severity,
    string? Status,
    string? Code,
    string? Message,
    DateTimeOffset OccurredAt
);

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

/// <summary>
/// The ONE flat response POST /api/integrations/whatsapp/webhook returns to n8n — see
/// Integrations/WhatsApp/MetaWebhookProcessor.cs. Deliberately not an array/wrapper: n8n's entire
/// job after calling this endpoint is checking ShouldRunAi, nothing else. Fields that don't apply
/// to a given EventType are null. When the raw payload contained multiple events, this represents
/// either the most recent AI-eligible customer message (if any), or the last event processed —
/// every contained event is still persisted/broadcast internally regardless of which one is
/// reflected here (see the processor for the selection rule).
/// </summary>
public record WhatsAppWebhookResponse(
    bool Processed,
    string EventType,
    bool ShouldRunAi,
    Guid? ClinicId,
    Guid? WhatsAppConnectionId = null,
    Guid? ConversationId = null,
    Guid? LeadId = null,
    Guid? MessageId = null,
    string? ExternalMessageId = null,
    string? Mode = null,
    string? Status = null,
    Guid? TemplateId = null,
    string? MetaTemplateId = null,
    string? TemplateName = null,
    string? HealthLevel = null,
    string? HealthEventType = null,
    string? Message = null,
    /// <summary>CustomerMessage only — carried so the caller (WhatsAppWebhookController) can build
    /// the normalized AI-trigger payload for n8n straight from this response, with no extra lookup.</summary>
    string? MessageType = null,
    string? Content = null,
    /// <summary>Interactive/button replies only — the stable Meta reply id (e.g. "rhinoplasty"),
    /// separate from Content (the user-visible title sent as messageText). Null otherwise.</summary>
    string? SelectedValue = null
);
