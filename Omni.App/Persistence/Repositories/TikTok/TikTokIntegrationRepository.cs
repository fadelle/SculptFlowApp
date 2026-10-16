using Microsoft.EntityFrameworkCore;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Persistence.Helpers;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.TikTok;

namespace Omni.App.Persistence.Repositories.TikTok;

public class TikTokIntegrationRepository : ITikTokIntegrationRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ICacheManager? _cache;

    public TikTokIntegrationRepository(ApplicationDbContext db, ICacheManager? cache = null)
    {
        _db = db;
        _cache = cache;
    }

    public Task<TikTokIntegration?> GetAsync(Guid clientId, CancellationToken ct = default) =>
        _db.TikTokIntegrations.FirstOrDefaultAsync(t => t.ClientId == clientId, ct);

    public Task<TikTokIntegration?> GetReadOnlyAsync(Guid clientId, CancellationToken ct = default) =>
        _cache.ReadThroughValueAsync(CacheKeys.TikTok(clientId), CacheArea.Channels,
            token => _db.TikTokIntegrations.AsNoTracking().FirstOrDefaultAsync(t => t.ClientId == clientId, token), ct);

    public void Add(TikTokIntegration integration) => _db.TikTokIntegrations.Add(integration);
}
