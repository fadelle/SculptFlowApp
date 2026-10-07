using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.Billing;

namespace PlasticSurgery.Entities.Responses.Billing;

public record ClinicBillingSummary(
    bool BillingEnabled,
    string Currency,
    string? PlanCode,
    string? PlanName,
    decimal? PlanPrice,
    string? BillingPeriod,
    string? SubscriptionStatus,
    bool HasAccess,
    DateTimeOffset? CurrentPeriodStart,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    DateTimeOffset? GraceEndsAt,
    decimal PlanIncludedCredit,
    decimal IncludedCreditRemaining,
    decimal WalletBalance,
    decimal ReservedAmount,
    decimal Spendable,
    IReadOnlyList<BillingEntitlementRow> Entitlements,
    DateTimeOffset UsageSince,
    IReadOnlyList<BillingUsageBreakdownRow> Usage,
    decimal UsageTotal,
    IReadOnlyList<BillingTransactionRow> RecentTransactions,
    IReadOnlyList<ClinicChannelBillingRow> ChannelAccounts,
    IReadOnlyList<ProviderDirectUsageRow> ProviderDirectUsage);
