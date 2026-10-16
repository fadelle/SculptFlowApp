using Microsoft.Extensions.Options;
using Omni.App.Entities.Dtos.Billing;

namespace Omni.App.Business.Contracts.Services.Billing;

/// <summary>
/// Provider billing responsibility — question 2 of the three billing questions (1. what the clinic pays Omni for
/// access = subscription; 2. who pays the upstream provider = this; 3. does Omni charge for the usage). Resolved
/// per CONNECTED CHANNEL ACCOUNT (a channel_integrations row), never assumed per channel:
///   account override (billing.channel_account_settings)
///   -> Billing:ProviderBilling:Defaults "{channel}_{provider}" -> "{channel}" -> built-in defaults below.
/// Omni usage billing defaults to on only when Omni pays the provider; an override can turn it on for a
/// customer-paid account (an Omni usage fee, priced by rates set for that responsibility) or off for a funded one.
/// </summary>
public interface IProviderBillingService
{
    /// <summary>The arrangement for the clinic's account on this channel (provider = the one the usage goes through).</summary>
    Task<ProviderBillingSetting> ResolveAsync(Guid clinicId, string channel, string? provider, CancellationToken ct = default);

    /// <summary>The default for a channel/provider pair (no account override).</summary>
    string DefaultFor(string channel, string? provider);

    /// <summary>Every connected (or previously connected) channel account of the clinic with its arrangement.</summary>
    Task<IReadOnlyList<ChannelAccountBilling>> ListAccountsAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Sets an account's override (applies to future usage only; recorded usage keeps its snapshot).</summary>
    Task<ChannelAccountBilling?> SetAsync(Guid channelIntegrationId, ChannelAccountBillingChange change, CancellationToken ct = default);

    /// <summary>Removes an account's override (back to the defaults).</summary>
    Task<ChannelAccountBilling?> ResetAsync(Guid channelIntegrationId, string reason, string? actor, CancellationToken ct = default);
}
