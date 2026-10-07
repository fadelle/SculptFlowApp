using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Dtos.Billing;

public record AdminUsageRow(Guid Id, string IdempotencyKey, DateTimeOffset OccurredAt, string EventType, string Channel,
    Guid? ChannelIntegrationId, string ProviderBilling, decimal Quantity,
    string? Unit, string? CountryCode, string? Operator, string? Provider, string? RateSource, Guid? RateId, decimal? UnitProviderCost,
    decimal? UnitPrice, decimal? ProviderCost, decimal? Amount, decimal ReservedAmount, decimal CreditAmount, decimal WalletAmount,
    decimal RefundedAmount, string Currency, string ChargeStatus, string ProviderOutcome, string? FailureReason, string? ReleaseReason, Guid? MessageId,
    Guid? ConversationId, Guid? CampaignId, string Source, string? Actor, DateTimeOffset? ReservedAt, DateTimeOffset? SettledAt,
    DateTimeOffset? ReleasedAt, DateTimeOffset? RefundedAt);
