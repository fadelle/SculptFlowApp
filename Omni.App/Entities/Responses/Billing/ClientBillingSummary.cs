using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.Billing;

namespace Omni.App.Entities.Responses.Billing;

public record ClientBillingSummary(
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
    IReadOnlyList<ClientChannelBillingRow> ChannelAccounts,
    IReadOnlyList<ProviderDirectUsageRow> ProviderDirectUsage,
    int? TrialDays = null,
    IReadOnlyList<ClientAddonRow>? Addons = null,
    // When an expired or cancelled subscription ended (QA t2-billing-15: the page said "Renews on" for one).
    DateTimeOffset? EndedAt = null);
