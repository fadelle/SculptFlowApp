namespace PlasticSurgery.Entities.Dtos.Billing;

/// <param name="Amount">What SculptFlow charged (0 for usage it doesn't bill).</param>
/// <param name="ProviderCost">The provider's cost where known, whoever paid it (see ProviderBilling).</param>
/// <param name="Margin">Amount minus the provider cost SculptFlow itself paid.</param>
public record AdminUsageBreakdownRow(string EventType, string Channel, string ProviderBilling, int Count, decimal Quantity, decimal Amount,
    decimal ProviderCost, decimal Margin);
