namespace PlasticSurgery.Entities.Requests.Billing;

/// <summary>Set who pays the provider for one connected channel account, and/or whether SculptFlow charges usage on
/// it. Null = keep the default for that part. Reason is required (audit).</summary>
public record ProviderBillingRequest(string? ProviderBilling, bool? OmniUsageBilling, string Reason);
