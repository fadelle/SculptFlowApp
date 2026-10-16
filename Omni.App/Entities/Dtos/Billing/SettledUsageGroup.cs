namespace Omni.App.Entities.Dtos.Billing;

/// <summary>Usage Omni charged, grouped by event type (Amount = settled minus refunded).</summary>
public record SettledUsageGroup(string EventType, int Count, decimal Quantity, decimal Amount);
