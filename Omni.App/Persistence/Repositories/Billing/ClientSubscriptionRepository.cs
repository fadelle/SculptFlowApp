using Microsoft.EntityFrameworkCore;
using Omni.App.Common.Enums;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Billing;

namespace Omni.App.Persistence.Repositories.Billing;

public class ClientSubscriptionRepository : IClientSubscriptionRepository
{
    private readonly ApplicationDbContext _db;

    public ClientSubscriptionRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public void Add(ClientSubscription subscription) => _db.ClientSubscriptions.Add(subscription);

    public Task<ClientSubscription?> GetWithPlanAsync(Guid clientId, CancellationToken ct = default) =>
        _db.ClientSubscriptions.Include(s => s.Plan).FirstOrDefaultAsync(s => s.ClientId == clientId, ct);

    public Task<ClientSubscription?> GetWithPlanReadOnlyAsync(Guid clientId, CancellationToken ct = default) =>
        _db.ClientSubscriptions.AsNoTracking().Include(s => s.Plan).FirstOrDefaultAsync(s => s.ClientId == clientId, ct);

    public Task<ClientSubscription?> GetWithEntitlementsReadOnlyAsync(Guid clientId, CancellationToken ct = default) =>
        _db.ClientSubscriptions.AsNoTracking()
            .Include(s => s.Plan).ThenInclude(p => p!.Entitlements)
            .FirstOrDefaultAsync(s => s.ClientId == clientId, ct);

    public async Task<IReadOnlyDictionary<Guid, ClientSubscription>> MapAllWithPlanReadOnlyAsync(CancellationToken ct = default) =>
        await _db.ClientSubscriptions.AsNoTracking().Include(s => s.Plan).ToDictionaryAsync(s => s.ClientId, ct);

    public async Task<IReadOnlyList<Guid>> ListDueClientIdsAsync(DateTimeOffset now, CancellationToken ct = default) =>
        await _db.ClientSubscriptions.AsNoTracking()
            .Where(s => (s.Status == SubscriptionStatus.Active && s.CurrentPeriodEnd <= now) || s.Status == SubscriptionStatus.PastDue)
            .Select(s => s.ClientId)
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, int>> CountSubscribersByPlanAsync(CancellationToken ct = default) =>
        await _db.ClientSubscriptions.AsNoTracking()
            .Where(s => s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue)
            .GroupBy(s => s.PlanId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
}
