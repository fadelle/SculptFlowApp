using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Persistence.Helpers;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Billing;

namespace PlasticSurgery.Persistence.Repositories.Billing;

public class ChannelAccountBillingRepository : IChannelAccountBillingRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ICacheManager? _cache;

    public ChannelAccountBillingRepository(ApplicationDbContext db, ICacheManager? cache = null)
    {
        _db = db;
        _cache = cache;
    }

    public Task<ChannelAccountBillingSettings?> GetReadOnlyAsync(Guid channelIntegrationId, CancellationToken ct = default) =>
        _cache.ReadThroughValueAsync(CacheKeys.ChannelAccountBilling(channelIntegrationId), CacheArea.BillingRates,
            token => _db.ChannelAccountBillingSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.ChannelIntegrationId == channelIntegrationId, token), ct);

    public Task<ChannelAccountBillingSettings?> GetAsync(Guid channelIntegrationId, CancellationToken ct = default) =>
        _db.ChannelAccountBillingSettings.FirstOrDefaultAsync(s => s.ChannelIntegrationId == channelIntegrationId, ct);

    public async Task<IReadOnlyDictionary<Guid, ChannelAccountBillingSettings>> MapReadOnlyAsync(
        IReadOnlyCollection<Guid> channelIntegrationIds, CancellationToken ct = default) =>
        await _db.ChannelAccountBillingSettings.AsNoTracking()
            .Where(s => channelIntegrationIds.Contains(s.ChannelIntegrationId)).ToDictionaryAsync(s => s.ChannelIntegrationId, ct);

    public void Add(ChannelAccountBillingSettings settings) => _db.ChannelAccountBillingSettings.Add(settings);

    public void Remove(ChannelAccountBillingSettings settings) => _db.ChannelAccountBillingSettings.Remove(settings);
}
