namespace PlasticSurgery.Entities.Dtos.Billing;

public record AdminLedgerRow(Guid Id, DateTimeOffset CreatedAt, string EntryType, string BalanceType, decimal Amount, decimal BalanceAfter,
    string Currency, string IdempotencyKey, Guid? UsageRecordId, Guid? SubscriptionId, Guid? PlanId, string Source, string? Actor,
    string? Reason, string? Reference, string? CorrelationId);
