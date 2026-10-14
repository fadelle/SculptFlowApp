using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Persistence.Helpers;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Channels;

namespace PlasticSurgery.Persistence.Repositories.Channels;

public class ChannelIntegrationRepository : IChannelIntegrationRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ICacheManager? _cache;

    public ChannelIntegrationRepository(ApplicationDbContext db, ICacheManager? cache = null)
    {
        _db = db;
        _cache = cache;
    }

    public void Add(ChannelIntegration integration) => _db.ChannelIntegrations.Add(integration);

    public Task<ChannelIntegration?> GetAsync(Guid clinicId, string channel, CancellationToken ct = default) =>
        _db.ChannelIntegrations.FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.Channel == channel, ct);

    public async Task<ChannelIntegration?> GetReadOnlyAsync(Guid clinicId, string channel, CancellationToken ct = default) =>
        (await ListForClinicReadOnlyAsync(clinicId, ct)).FirstOrDefault(c => c.Channel == channel);

    public Task<ChannelIntegration?> GetByIdReadOnlyAsync(Guid integrationId, CancellationToken ct = default) =>
        // Unknown ids aren't cached (null isn't), so webhook calls with made-up ids can't fill the cache.
        _cache.ReadThroughAsync(CacheKeys.ChannelIntegration(integrationId), CacheArea.Channels,
            token => _db.ChannelIntegrations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == integrationId, token), ct);

    public async Task<IReadOnlyList<ChannelIntegration>> ListForClinicAsync(Guid clinicId, CancellationToken ct = default) =>
        await _db.ChannelIntegrations.Where(c => c.ClinicId == clinicId).ToListAsync(ct);

    public async Task<Guid> GetIdAsync(Guid clinicId, string channel, CancellationToken ct = default) =>
        (await GetReadOnlyAsync(clinicId, channel, ct))?.Id ?? Guid.Empty;

    public Task<ChannelIntegration?> FindWhatsAppByPhoneNumberIdAsync(string phoneNumberId, CancellationToken ct = default) =>
        _db.ChannelIntegrations.FirstOrDefaultAsync(
            c => c.Channel == ChannelType.WhatsApp && c.PhoneNumberId == phoneNumberId, ct);

    public Task<ChannelIntegration?> FindWhatsAppByWabaIdAsync(string wabaId, CancellationToken ct = default) =>
        _db.ChannelIntegrations.FirstOrDefaultAsync(
            c => c.Channel == ChannelType.WhatsApp && c.WhatsAppBusinessId == wabaId, ct);

    public Task<bool> IsTelegramBotConnectedElsewhereAsync(string botId, Guid clinicId, CancellationToken ct = default) =>
        _db.ChannelIntegrations.AnyAsync(c =>
            c.Channel == ChannelType.Telegram && c.TelegramBotId == botId
            && c.Status == ChannelIntegrationStatus.Connected && c.ClinicId != clinicId, ct);

    public async Task<int> CountConnectedExceptAsync(Guid clinicId, string exceptChannel, CancellationToken ct = default) =>
        (await ListForClinicReadOnlyAsync(clinicId, ct))
            .Count(c => c.Channel != exceptChannel && c.Status == ChannelIntegrationStatus.Connected);

    public Task TouchLastWebhookAsync(Guid integrationId, DateTimeOffset at, CancellationToken ct = default) =>
        _db.ChannelIntegrations.Where(c => c.Id == integrationId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastWebhookAt, at), ct);

    public async Task<IReadOnlyList<ChannelIntegration>> ListForClinicReadOnlyAsync(Guid clinicId, CancellationToken ct = default) =>
        (await _cache.ReadThroughAsync(CacheKeys.ChannelIntegrations(clinicId), CacheArea.Channels,
            async token => await _db.ChannelIntegrations.AsNoTracking().Where(c => c.ClinicId == clinicId)
                .OrderBy(c => c.Channel).ToListAsync(token), ct))!;
}
