namespace Omni.App.Entities.Dtos.Billing;

/// <summary>Usage Omni recorded but didn't charge, grouped by event type, channel and who pays the provider.</summary>
public record UnchargedUsageGroup(string EventType, string Channel, string ProviderBilling, int Count, decimal Quantity);
