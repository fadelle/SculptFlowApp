using Microsoft.Extensions.Options;

namespace PlasticSurgery.Entities.Dtos.Billing;

/// <summary>An admin change to one account's arrangement. Null fields keep the default; Reason is required.</summary>
public sealed record ChannelAccountBillingChange(string? ProviderBilling, bool? OmniUsageBilling, string Reason, string? Actor);
