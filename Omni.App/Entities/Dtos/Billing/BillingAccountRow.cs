using Omni.App.Common.Enums;

namespace Omni.App.Entities.Dtos.Billing;

public record BillingAccountRow(Guid ClientId, string ClientName, string? PlanCode, string? SubscriptionStatus, DateTimeOffset? CurrentPeriodEnd,
    decimal WalletBalance, decimal IncludedCreditBalance, decimal ReservedAmount, string Currency);
