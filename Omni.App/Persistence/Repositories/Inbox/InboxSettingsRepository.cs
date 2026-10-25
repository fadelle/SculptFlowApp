using Microsoft.EntityFrameworkCore;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Inbox;
using Omni.App.Persistence.Helpers;

namespace Omni.App.Persistence.Repositories.Inbox;

public class InboxSettingsRepository : IInboxSettingsRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ICacheManager? _cache;

    public InboxSettingsRepository(ApplicationDbContext db, ICacheManager? cache = null)
    {
        _db = db;
        _cache = cache;
    }

    // Read on every incoming contact message, so the client's rows (or none) are cached.
    public async Task<IReadOnlyList<InboxSettings>> ListForClientReadOnlyAsync(Guid clientId, CancellationToken ct = default) =>
        (await _cache.ReadThroughAsync(CacheKeys.InboxSettings(clientId), CacheArea.Clients,
            async token => await _db.InboxSettings.AsNoTracking().Where(s => s.ClientId == clientId)
                .OrderBy(s => s.CreatedAt).ToListAsync(token), ct))!;

    public Task<InboxSettings?> GetForAccountAsync(Guid clientId, Guid channelIntegrationId, CancellationToken ct = default) =>
        _db.InboxSettings.FirstOrDefaultAsync(s => s.ClientId == clientId && s.ChannelIntegrationId == channelIntegrationId, ct);

    public async Task<IReadOnlyList<InboxSettings>> ListForBotAsync(Guid clientId, Guid botId, CancellationToken ct = default) =>
        await _db.InboxSettings.Where(s => s.ClientId == clientId && s.BotId == botId).ToListAsync(ct);

    public void Add(InboxSettings settings) => _db.InboxSettings.Add(settings);
}
