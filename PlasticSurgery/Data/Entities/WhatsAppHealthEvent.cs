namespace PlasticSurgery.Data.Entities;

/// <summary>
/// History row for one Meta account/phone-number health webhook (account_update,
/// account_review_update, phone_number_quality_update, phone_number_name_update, etc.) — see
/// WhatsAppHealthService.ApplyHealthEventAsync. ChannelIntegration holds only the *current* state;
/// this table is the append-only trail behind it, kept even after ChannelIntegration moves on, so
/// staff can see "what changed and when" on /WhatsApp/Health.
/// </summary>
public class WhatsAppHealthEvent
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public Guid ChannelIntegrationId { get; set; }

    public string EventType { get; set; } = string.Empty;
    /// <summary>One of WhatsAppHealthEventSeverity — normalized here; RawMetadataJson keeps
    /// whatever Meta actually sent for cases the normalization doesn't fully capture.</summary>
    public string? Severity { get; set; }

    public string? Status { get; set; }
    public string? Code { get; set; }
    public string? Message { get; set; }

    /// <summary>Raw/normalized payload n8n forwarded, verbatim — the "keep raw metadata" half of
    /// "normalize carefully while keeping raw metadata".</summary>
    public string? RawMetadataJson { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ChannelIntegration? ChannelIntegration { get; set; }
}

/// <summary>Known event_type values — free text at the DB level (Meta may add more), these are
/// just the ones WhatsAppHealthService currently knows how to normalize into a severity/HealthLevel.</summary>
public static class WhatsAppHealthEventType
{
    public const string PhoneNumberQualityUpdate = "phone_number_quality_update";
    public const string PhoneNumberNameUpdate = "phone_number_name_update";
    public const string AccountUpdate = "account_update";
    public const string AccountReviewUpdate = "account_review_update";
    public const string ConnectionUpdate = "connection_update";
    public const string TemplateProblem = "template_problem";
    public const string WebhookProblem = "webhook_problem";
    public const string UnknownMetaHealthEvent = "unknown_meta_health_event";
}

/// <summary>Free text at the DB level — not every Meta status maps cleanly to one of these, so
/// WhatsAppHealthService picks the closest fit rather than failing on anything unrecognized.</summary>
public static class WhatsAppHealthEventSeverity
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Critical = "critical";
}
