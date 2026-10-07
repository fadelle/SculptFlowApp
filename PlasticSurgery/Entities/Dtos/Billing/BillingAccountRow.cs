using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Dtos.Billing;

public record BillingAccountRow(Guid ClinicId, string ClinicName, string? PlanCode, string? SubscriptionStatus, DateTimeOffset? CurrentPeriodEnd,
    decimal WalletBalance, decimal IncludedCreditBalance, decimal ReservedAmount, string Currency);
