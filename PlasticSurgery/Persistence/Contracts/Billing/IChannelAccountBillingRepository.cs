using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.Billing;

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
