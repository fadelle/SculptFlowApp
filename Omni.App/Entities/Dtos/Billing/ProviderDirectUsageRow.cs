namespace Omni.App.Entities.Dtos.Billing;

/// <summary>Usage Omni didn't charge for (the client pays the provider directly, or there's no per-message fee).
/// Shown apart from Omni charges so it never looks like it came out of the wallet.</summary>
public record ProviderDirectUsageRow(string EventType, string Label, string PaidBy, int Count, decimal Quantity);
