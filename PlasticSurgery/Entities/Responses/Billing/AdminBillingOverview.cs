using PlasticSurgery.Entities.Dtos.Billing;

namespace PlasticSurgery.Entities.Responses.Billing;

public record AdminBillingOverview(
    Guid ClinicId,
    string ClinicName,
    ClinicBillingSummary Summary,
    SubscriptionResponse? Subscription,
    string? CustomRateCardCode,
    IReadOnlyList<AdminUsageBreakdownRow> UsageWithCosts,
    int OpenReservations,
    ReconciliationResponse Reconciliation,
    IReadOnlyList<ChannelAccountBilling> ChannelAccounts);
