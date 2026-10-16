using Omni.App.Entities.Dtos.Billing;

namespace Omni.App.Entities.Responses.Billing;

public record BillingReport(DateTimeOffset From, DateTimeOffset To, string Currency, IReadOnlyList<BillingReportRow> Usage,
    decimal UsageRevenue, decimal ProviderCostPaidByPlatform, decimal ProviderCostPaidExternally, decimal Margin,
    decimal SubscriptionRevenue, decimal TopUps);
