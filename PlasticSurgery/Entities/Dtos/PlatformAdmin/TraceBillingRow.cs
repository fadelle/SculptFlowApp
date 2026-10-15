namespace PlasticSurgery.Entities.Dtos.PlatformAdmin;

/// <summary>What billing did for a message in a trace (billing.usage_records).</summary>
public record TraceBillingRow(
    Guid MessageId,
    string ChargeStatus,
    decimal? Amount,
    string Currency,
    DateTimeOffset OccurredAt,
    DateTimeOffset? ReservedAt,
    DateTimeOffset? SettledAt,
    DateTimeOffset? ReleasedAt,
    string? ReleaseReason,
    string? FailureReason);