using Microsoft.Extensions.Options;

namespace PlasticSurgery.Entities.Dtos.Billing;

/// <summary>One connected channel account with its billing arrangement (admin view).</summary>
public sealed record ChannelAccountBilling(
    Guid ChannelIntegrationId, string Channel, string? Provider, string Status, string? DisplayName,
    string ProviderBilling, string ProviderBillingLabel, bool OmniUsageBilling, string Source,
    string DefaultProviderBilling, string? OverrideProviderBilling, bool? OverrideOmniUsageBilling, string? OverrideAppliesToProvider,
    string? OverrideReason, string? OverrideUpdatedBy, DateTimeOffset? OverrideUpdatedAt);
