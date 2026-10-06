namespace PlasticSurgery.Entities.Dtos.Billing;

public record BillingTransactionRow(Guid Id, DateTimeOffset CreatedAt, string EntryType, string Label, string BalanceType,
    decimal Amount, decimal BalanceAfter, string? Reason);
