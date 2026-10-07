using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Billing;

namespace PlasticSurgery.Persistence.Repositories.Billing;

public class ChannelAccountBillingRepository : IChannelAccountBillingRepository
{
    private readonly ApplicationDbContext _db;

    public ChannelAccountBillingRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<ChannelAccountBillingSettings?> GetReadOnlyAsync(Guid channelIntegrationId, CancellationToken ct = default) =>
        _db.ChannelAccountBillingSettings.AsNoTracking().FirstOrDefaultAsync(s => s.ChannelIntegrationId == channelIntegrationId, ct);

    public Task<ChannelAccountBillingSettings?> GetAsync(Guid channelIntegrationId, CancellationToken ct = default) =>
        _db.ChannelAccountBillingSettings.FirstOrDefaultAsync(s => s.ChannelIntegrationId == channelIntegrationId, ct);

    public async Task<IReadOnlyDictionary<Guid, ChannelAccountBillingSettings>> MapReadOnlyAsync(
        IReadOnlyCollection<Guid> channelIntegrationIds, CancellationToken ct = default) =>
        await _db.ChannelAccountBillingSettings.AsNoTracking()
            .Where(s => channelIntegrationIds.Contains(s.ChannelIntegrationId)).ToDictionaryAsync(s => s.ChannelIntegrationId, ct);

    public void Add(ChannelAccountBillingSettings settings) => _db.ChannelAccountBillingSettings.Add(settings);

    public void Remove(ChannelAccountBillingSettings settings) => _db.ChannelAccountBillingSettings.Remove(settings);
}
