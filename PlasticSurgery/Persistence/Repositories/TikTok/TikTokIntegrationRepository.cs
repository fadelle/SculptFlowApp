using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Persistence.Helpers;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.TikTok;

namespace PlasticSurgery.Persistence.Repositories.TikTok;

public class TikTokIntegrationRepository : ITikTokIntegrationRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ICacheManager? _cache;

    public TikTokIntegrationRepository(ApplicationDbContext db, ICacheManager? cache = null)
    {
        _db = db;
        _cache = cache;
    }

    public Task<TikTokIntegration?> GetAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.TikTokIntegrations.FirstOrDefaultAsync(t => t.ClinicId == clinicId, ct);

    public Task<TikTokIntegration?> GetReadOnlyAsync(Guid clinicId, CancellationToken ct = default) =>
        _cache.ReadThroughValueAsync(CacheKeys.TikTok(clinicId), CacheArea.Channels,
            token => _db.TikTokIntegrations.AsNoTracking().FirstOrDefaultAsync(t => t.ClinicId == clinicId, token), ct);

    public void Add(TikTokIntegration integration) => _db.TikTokIntegrations.Add(integration);
}
