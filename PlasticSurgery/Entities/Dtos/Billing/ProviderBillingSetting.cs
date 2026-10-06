using Microsoft.Extensions.Options;

namespace PlasticSurgery.Entities.Dtos.Billing;

/// <summary>The resolved arrangement for one connected channel account.</summary>
/// <param name="Responsibility">Who pays the upstream provider (ProviderBillingResponsibility).</param>
/// <param name="ChargesUsage">Does SculptFlow charge the clinic for usage on this account?</param>
/// <param name="ChannelIntegrationId">The connected account (channel_integrations.id), when the clinic has one.</param>
/// <param name="Source">"account" when an admin override applies, else "default".</param>
public sealed record ProviderBillingSetting(string Responsibility, bool ChargesUsage, Guid? ChannelIntegrationId, string Source);
