namespace PlasticSurgery.Entities.Models;

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
