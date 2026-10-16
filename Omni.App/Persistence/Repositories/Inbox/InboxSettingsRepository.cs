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

    public Task<InboxSettings?> GetForClientAsync(Guid clientId, CancellationToken ct = default) =>
        _db.InboxSettings.FirstOrDefaultAsync(s => s.ClientId == clientId && s.ChannelIntegrationId == null, ct);

    // Read on every incoming patient message, so the client-wide row (or its absence) is cached.
    public Task<InboxSettings?> GetForClientReadOnlyAsync(Guid clientId, CancellationToken ct = default) =>
        _cache.ReadThroughValueAsync(CacheKeys.InboxSettings(clientId), CacheArea.Clients,
            token => _db.InboxSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.ClientId == clientId && s.ChannelIntegrationId == null, token), ct);

    public void Add(InboxSettings settings) => _db.InboxSettings.Add(settings);
}
