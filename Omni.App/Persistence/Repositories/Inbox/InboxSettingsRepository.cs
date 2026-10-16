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

    public Task<InboxSettings?> GetForClinicAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.InboxSettings.FirstOrDefaultAsync(s => s.ClinicId == clinicId && s.ChannelIntegrationId == null, ct);

    // Read on every incoming patient message, so the clinic-wide row (or its absence) is cached.
    public Task<InboxSettings?> GetForClinicReadOnlyAsync(Guid clinicId, CancellationToken ct = default) =>
        _cache.ReadThroughValueAsync(CacheKeys.InboxSettings(clinicId), CacheArea.Clinics,
            token => _db.InboxSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.ClinicId == clinicId && s.ChannelIntegrationId == null, token), ct);

    public void Add(InboxSettings settings) => _db.InboxSettings.Add(settings);
}
