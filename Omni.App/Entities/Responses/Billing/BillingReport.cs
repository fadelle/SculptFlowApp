using Omni.App.Entities.Dtos.Billing;

namespace Omni.App.Entities.Responses.Billing;

public record BillingReport(DateTimeOffset From, DateTimeOffset To, string Currency, IReadOnlyList<BillingReportRow> Usage,
    decimal UsageRevenue, decimal ProviderCostPaidByPlatform, decimal ProviderCostPaidExternally, decimal Margin,
    decimal SubscriptionRevenue, decimal TopUps,
    // QA t2-billing-09: recurring add-on revenue (addon_charge), the net of manual wallet adjustments (positive = credit given)
    // and the usage money refunded to wallets (already netted out of UsageRevenue; shown for reconciliation).
    decimal AddonRevenue = 0, decimal ManualAdjustments = 0, decimal UsageRefunds = 0);
