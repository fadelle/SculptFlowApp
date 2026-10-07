namespace PlasticSurgery.Entities.Dtos.Billing;

/// <summary>Provider-billable usage of one clinic grouped by event type, channel and who pays the provider.
/// Amount = what SculptFlow charged (settled minus refunded); Cost = provider cost.</summary>
public record BillableUsageGroup(string EventType, string Channel, string ProviderBilling, int Count, decimal Quantity,
    decimal Amount, decimal Cost);
