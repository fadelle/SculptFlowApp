using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Billing;

namespace PlasticSurgery.Persistence.Repositories.Billing;

public class BillingPlanRepository : IBillingPlanRepository
{
    private readonly ApplicationDbContext _db;

    public BillingPlanRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListWithEntitlementsReadOnlyAsync(CancellationToken ct = default) =>
        await _db.SubscriptionPlans.AsNoTracking().Include(p => p.Entitlements)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Price).ToListAsync(ct);

    public Task<bool> CodeExistsAsync(string code, CancellationToken ct = default) =>
        _db.SubscriptionPlans.AnyAsync(p => p.Code == code, ct);

    public Task<SubscriptionPlan?> GetWithEntitlementsAsync(string code, CancellationToken ct = default) =>
        _db.SubscriptionPlans.Include(p => p.Entitlements).FirstOrDefaultAsync(p => p.Code == code, ct);

    public Task<SubscriptionPlan?> GetAsync(string code, CancellationToken ct = default) =>
        _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Code == code, ct);

    public void Add(SubscriptionPlan plan) => _db.SubscriptionPlans.Add(plan);

    public void AddEntitlement(SubscriptionPlanEntitlement entitlement) => _db.SubscriptionPlanEntitlements.Add(entitlement);

    public void RemoveEntitlement(SubscriptionPlanEntitlement entitlement) => _db.SubscriptionPlanEntitlements.Remove(entitlement);
}
