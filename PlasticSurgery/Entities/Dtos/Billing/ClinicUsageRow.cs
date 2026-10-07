using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Dtos.Billing;

public record ClinicUsageRow(Guid Id, DateTimeOffset OccurredAt, string EventType, string Label, string Channel, decimal Quantity,
    string? Unit, string? CountryCode, decimal? UnitPrice, decimal? Amount, decimal CreditAmount, decimal WalletAmount,
    decimal RefundedAmount, string ChargeStatus, string? FailureReason, bool ChargedBySculptFlow, string PaidBy);
