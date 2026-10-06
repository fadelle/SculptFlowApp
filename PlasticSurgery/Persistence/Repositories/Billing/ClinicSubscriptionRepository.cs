using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Billing;

namespace PlasticSurgery.Persistence.Repositories.Billing;

public class ClinicSubscriptionRepository : IClinicSubscriptionRepository
{
    private readonly ApplicationDbContext _db;

    public ClinicSubscriptionRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public void Add(ClinicSubscription subscription) => _db.ClinicSubscriptions.Add(subscription);

    public Task<ClinicSubscription?> GetWithPlanAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.ClinicSubscriptions.Include(s => s.Plan).FirstOrDefaultAsync(s => s.ClinicId == clinicId, ct);

    public Task<ClinicSubscription?> GetWithPlanReadOnlyAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.ClinicSubscriptions.AsNoTracking().Include(s => s.Plan).FirstOrDefaultAsync(s => s.ClinicId == clinicId, ct);

    public Task<ClinicSubscription?> GetWithEntitlementsReadOnlyAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.ClinicSubscriptions.AsNoTracking()
            .Include(s => s.Plan).ThenInclude(p => p!.Entitlements)
            .FirstOrDefaultAsync(s => s.ClinicId == clinicId, ct);

    public async Task<IReadOnlyDictionary<Guid, ClinicSubscription>> MapAllWithPlanReadOnlyAsync(CancellationToken ct = default) =>
        await _db.ClinicSubscriptions.AsNoTracking().Include(s => s.Plan).ToDictionaryAsync(s => s.ClinicId, ct);

    public async Task<IReadOnlyList<Guid>> ListDueClinicIdsAsync(DateTimeOffset now, CancellationToken ct = default) =>
        await _db.ClinicSubscriptions.AsNoTracking()
            .Where(s => (s.Status == SubscriptionStatus.Active && s.CurrentPeriodEnd <= now) || s.Status == SubscriptionStatus.PastDue)
            .Select(s => s.ClinicId)
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, int>> CountSubscribersByPlanAsync(CancellationToken ct = default) =>
        await _db.ClinicSubscriptions.AsNoTracking()
            .Where(s => s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue)
            .GroupBy(s => s.PlanId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
}
