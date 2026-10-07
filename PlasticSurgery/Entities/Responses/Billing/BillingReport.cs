using PlasticSurgery.Entities.Dtos.Billing;

namespace PlasticSurgery.Entities.Responses.Billing;

public record BillingReport(DateTimeOffset From, DateTimeOffset To, string Currency, IReadOnlyList<BillingReportRow> Usage,
    decimal UsageRevenue, decimal ProviderCostPaidBySculptFlow, decimal ProviderCostPaidExternally, decimal Margin,
    decimal SubscriptionRevenue, decimal TopUps);
