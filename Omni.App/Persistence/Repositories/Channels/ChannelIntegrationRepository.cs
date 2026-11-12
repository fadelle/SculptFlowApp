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

    // A disconnected row keeps its ids (only connected numbers are unique), so the connected row wins when both exist.
    public async Task<IReadOnlyList<ChannelIntegration>> ListConnectedWhatsAppByPhoneNumberIdAsync(string phoneNumberId, CancellationToken ct = default) =>
        await _db.ChannelIntegrations.AsNoTracking()
            .Where(c => c.Channel == ChannelType.WhatsApp && c.Status == ChannelIntegrationStatus.Connected && c.PhoneNumberId == phoneNumberId)
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ChannelIntegration>> ListConnectedWhatsAppByWabaIdAsync(string wabaId, CancellationToken ct = default) =>
        await _db.ChannelIntegrations.AsNoTracking()
            .Where(c => c.Channel == ChannelType.WhatsApp && c.Status == ChannelIntegrationStatus.Connected && c.WhatsAppBusinessId == wabaId)
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync(ct);

    public Task<bool> IsTelegramBotConnectedElsewhereAsync(string botId, Guid clientId, CancellationToken ct = default) =>
        _db.ChannelIntegrations.AnyAsync(c =>
            c.Channel == ChannelType.Telegram && c.TelegramBotId == botId
            && c.Status == ChannelIntegrationStatus.Connected && c.ClientId != clientId, ct);

    public async Task<int> CountConnectedExceptAsync(Guid clientId, string exceptChannel, CancellationToken ct = default) =>
        (await ListForClientReadOnlyAsync(clientId, ct))
            .Count(c => c.Channel != exceptChannel && c.Status == ChannelIntegrationStatus.Connected);

    public async Task<bool> MarkSendProblemAsync(ChannelIntegration integration, string code, string message, DateTimeOffset at, CancellationToken ct = default)
    {
        var text = message.Length > 1000 ? message[..1000] : message;
        var moved = await _db.ChannelIntegrations
            .Where(c => c.Id == integration.Id && c.HealthLevel != WhatsAppHealthLevel.Problem && c.HealthLevel != WhatsAppHealthLevel.Disconnected)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsHealthy, false).SetProperty(c => c.HealthLevel, WhatsAppHealthLevel.Problem)
                .SetProperty(c => c.LastProblemCode, code).SetProperty(c => c.LastProblemMessage, text).SetProperty(c => c.LastError, text)
                .SetProperty(c => c.UpdatedAt, at), ct) > 0;
        if (!moved)
        {
            // Already a problem: keep its newest words.
            await _db.ChannelIntegrations.Where(c => c.Id == integration.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastError, text).SetProperty(c => c.UpdatedAt, at), ct);
        }
        if (_cache is not null)
        {
            await _cache.RemoveAsync(CacheKeys.ChannelIntegrations(integration.ClientId), ct);
            await _cache.RemoveAsync(CacheKeys.ChannelIntegration(integration.Id), ct);
        }
        return moved;
    }

    public Task TouchLastWebhookAsync(Guid integrationId, DateTimeOffset at, CancellationToken ct = default) =>
        _db.ChannelIntegrations.Where(c => c.Id == integrationId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastWebhookAt, at), ct);

    public async Task<IReadOnlyList<ChannelIntegration>> ListForClientReadOnlyAsync(Guid clientId, CancellationToken ct = default) =>
        (await _cache.ReadThroughAsync(CacheKeys.ChannelIntegrations(clientId), CacheArea.Channels,
            async token => await _db.ChannelIntegrations.AsNoTracking().Where(c => c.ClientId == clientId)
                .OrderBy(c => c.Channel).ToListAsync(token), ct))!;
}
