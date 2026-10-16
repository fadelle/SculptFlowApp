using Omni.App.Common.Enums;

namespace Omni.App.Entities.Dtos.Billing;

public record ClientUsageRow(Guid Id, DateTimeOffset OccurredAt, string EventType, string Label, string Channel, decimal Quantity,
    string? Unit, string? CountryCode, decimal? UnitPrice, decimal? Amount, decimal CreditAmount, decimal WalletAmount,
    decimal RefundedAmount, string ChargeStatus, string? FailureReason, bool ChargedByPlatform, string PaidBy);
