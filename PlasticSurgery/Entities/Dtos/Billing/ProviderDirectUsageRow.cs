namespace PlasticSurgery.Entities.Dtos.Billing;

/// <summary>Usage SculptFlow didn't charge for (the clinic pays the provider directly, or there's no per-message fee).
/// Shown apart from SculptFlow charges so it never looks like it came out of the wallet.</summary>
public record ProviderDirectUsageRow(string EventType, string Label, string PaidBy, int Count, decimal Quantity);
