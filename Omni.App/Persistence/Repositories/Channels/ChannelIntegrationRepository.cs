using Microsoft.EntityFrameworkCore;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Statics;
using Omni.App.Persistence.Helpers;
using Omni.App.Common.Enums;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Channels;

namespace Omni.App.Persistence.Repositories.Channels;

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

    public Task<ChannelIntegration?> GetAsync(Guid clientId, string channel, CancellationToken ct = default) =>
        _db.ChannelIntegrations.FirstOrDefaultAsync(c => c.ClientId == clientId && c.Channel == channel, ct);

    public async Task<ChannelIntegration?> GetReadOnlyAsync(Guid clientId, string channel, CancellationToken ct = default) =>
        (await ListForClientReadOnlyAsync(clientId, ct)).FirstOrDefault(c => c.Channel == channel);

    public Task<ChannelIntegration?> GetByIdReadOnlyAsync(Guid integrationId, CancellationToken ct = default) =>
        // Unknown ids aren't cached (null isn't), so webhook calls with made-up ids can't fill the cache.
        _cache.ReadThroughAsync(CacheKeys.ChannelIntegration(integrationId), CacheArea.Channels,
            token => _db.ChannelIntegrations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == integrationId, token), ct);

    public async Task<IReadOnlyList<ChannelIntegration>> ListForClientAsync(Guid clientId, CancellationToken ct = default) =>
        await _db.ChannelIntegrations.Where(c => c.ClientId == clientId).ToListAsync(ct);

    public async Task<Guid> GetIdAsync(Guid clientId, string channel, CancellationToken ct = default) =>
        (await GetReadOnlyAsync(clientId, channel, ct))?.Id ?? Guid.Empty;

    public Task<ChannelIntegration?> FindWhatsAppByPhoneNumberIdAsync(string phoneNumberId, CancellationToken ct = default) =>
        _db.ChannelIntegrations.FirstOrDefaultAsync(
            c => c.Channel == ChannelType.WhatsApp && c.PhoneNumberId == phoneNumberId, ct);

    public Task<ChannelIntegration?> FindWhatsAppByWabaIdAsync(string wabaId, CancellationToken ct = default) =>
        _db.ChannelIntegrations.FirstOrDefaultAsync(
            c => c.Channel == ChannelType.WhatsApp && c.WhatsAppBusinessId == wabaId, ct);

    public Task<bool> IsTelegramBotConnectedElsewhereAsync(string botId, Guid clientId, CancellationToken ct = default) =>
        _db.ChannelIntegrations.AnyAsync(c =>
            c.Channel == ChannelType.Telegram && c.TelegramBotId == botId
            && c.Status == ChannelIntegrationStatus.Connected && c.ClientId != clientId, ct);

    public async Task<int> CountConnectedExceptAsync(Guid clientId, string exceptChannel, CancellationToken ct = default) =>
        (await ListForClientReadOnlyAsync(clientId, ct))
            .Count(c => c.Channel != exceptChannel && c.Status == ChannelIntegrationStatus.Connected);

    public Task TouchLastWebhookAsync(Guid integrationId, DateTimeOffset at, CancellationToken ct = default) =>
        _db.ChannelIntegrations.Where(c => c.Id == integrationId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastWebhookAt, at), ct);

    public async Task<IReadOnlyList<ChannelIntegration>> ListForClientReadOnlyAsync(Guid clientId, CancellationToken ct = default) =>
        (await _cache.ReadThroughAsync(CacheKeys.ChannelIntegrations(clientId), CacheArea.Channels,
            async token => await _db.ChannelIntegrations.AsNoTracking().Where(c => c.ClientId == clientId)
                .OrderBy(c => c.Channel).ToListAsync(token), ct))!;
}
