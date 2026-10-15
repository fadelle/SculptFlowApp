using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Inbox;
using PlasticSurgery.Persistence.Helpers;

namespace PlasticSurgery.Persistence.Repositories.Inbox;

public class InboxSettingsRepository : IInboxSettingsRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ICacheManager? _cache;

    public InboxSettingsRepository(ApplicationDbContext db, ICacheManager? cache = null)
    {
        _db = db;
        _cache = cache;
    }

    public Task<InboxSettings?> GetForClinicAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.InboxSettings.FirstOrDefaultAsync(s => s.ClinicId == clinicId && s.ChannelIntegrationId == null, ct);

    // Read on every incoming patient message, so the clinic-wide row (or its absence) is cached.
    public Task<InboxSettings?> GetForClinicReadOnlyAsync(Guid clinicId, CancellationToken ct = default) =>
        _cache.ReadThroughValueAsync(CacheKeys.InboxSettings(clinicId), CacheArea.Clinics,
            token => _db.InboxSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.ClinicId == clinicId && s.ChannelIntegrationId == null, token), ct);

    public void Add(InboxSettings settings) => _db.InboxSettings.Add(settings);
}
