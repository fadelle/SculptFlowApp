using Microsoft.EntityFrameworkCore;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Billing;

namespace Omni.App.Persistence.Repositories.Billing;

public class BillingAddonRepository : IBillingAddonRepository
{
    private readonly ApplicationDbContext _db;

    public BillingAddonRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Addon>> ListCatalogReadOnlyAsync(CancellationToken ct = default) =>
        await _db.Addons.AsNoTracking().OrderBy(a => a.SortOrder).ThenBy(a => a.Code).ToListAsync(ct);

    public Task<Addon?> GetAsync(string code, CancellationToken ct = default) =>
        _db.Addons.FirstOrDefaultAsync(a => a.Code == code, ct);

    public void Add(Addon addon) => _db.Addons.Add(addon);

    public async Task<IReadOnlyDictionary<Guid, int>> CountClientsByAddonAsync(CancellationToken ct = default) =>
        await _db.ClientAddons.AsNoTracking().GroupBy(c => c.AddonId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    public async Task<IReadOnlyList<ClientAddon>> ListForClientReadOnlyAsync(Guid clientId, CancellationToken ct = default) =>
        await _db.ClientAddons.AsNoTracking().Include(c => c.Addon)
            .Where(c => c.ClientId == clientId)
            .OrderBy(c => c.Addon!.SortOrder)
            .ToListAsync(ct);

    public Task<ClientAddon?> GetForClientAsync(Guid clientId, Guid addonId, CancellationToken ct = default) =>
        _db.ClientAddons.FirstOrDefaultAsync(c => c.ClientId == clientId && c.AddonId == addonId, ct);

    public void AddForClient(ClientAddon clientAddon) => _db.ClientAddons.Add(clientAddon);

    public void RemoveForClient(ClientAddon clientAddon) => _db.ClientAddons.Remove(clientAddon);
}
