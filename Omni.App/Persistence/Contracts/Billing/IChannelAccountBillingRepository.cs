using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Billing;

/// <summary>Per connected channel account: who pays the provider (billing.channel_account_settings).</summary>
public interface IChannelAccountBillingRepository
{
    Task<ChannelAccountBillingSettings?> GetReadOnlyAsync(Guid channelIntegrationId, CancellationToken ct = default);

    /// <summary>Tracked.</summary>
    Task<ChannelAccountBillingSettings?> GetAsync(Guid channelIntegrationId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, ChannelAccountBillingSettings>> MapReadOnlyAsync(IReadOnlyCollection<Guid> channelIntegrationIds,
        CancellationToken ct = default);

    void Add(ChannelAccountBillingSettings settings);

    void Remove(ChannelAccountBillingSettings settings);
}
