using Microsoft.EntityFrameworkCore;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Persistence.Helpers;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Catalog;

namespace Omni.App.Persistence.Repositories.Catalog;

public class ServiceRepository : IServiceRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ICacheManager? _cache;

    public ServiceRepository(ApplicationDbContext db, ICacheManager? cache = null)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<IReadOnlyList<Service>> ListAsync(Guid clientId, bool activeOnly, CancellationToken ct = default)
    {
        var all = await ListAllReadOnlyAsync(clientId, ct);
        return activeOnly ? all.Where(p => p.IsActive).ToList() : all;
    }

    public Task<Service?> GetAsync(Guid clientId, Guid serviceId, CancellationToken ct = default) =>
        _db.Services.FirstOrDefaultAsync(p => p.ClientId == clientId && p.Id == serviceId, ct);

    public async Task<Service?> GetReadOnlyAsync(Guid clientId, Guid serviceId, CancellationToken ct = default) =>
        (await ListAllReadOnlyAsync(clientId, ct)).FirstOrDefault(p => p.Id == serviceId);

    public Task<bool> IsActiveAsync(Guid serviceId, CancellationToken ct = default) =>
        _db.Services.AnyAsync(p => p.Id == serviceId && p.IsActive, ct);

    public Task<bool> NameExistsAsync(Guid clientId, string name, Guid? exceptId, CancellationToken ct = default)
    {
        var lowered = name.ToLower();
        return _db.Services.AnyAsync(p => p.ClientId == clientId && p.Id != exceptId && p.Name.ToLower() == lowered, ct);
    }

    public void Add(Service service) => _db.Services.Add(service);

    /// <summary>Every service of the client by name, read-only (cached).</summary>
    private async Task<IReadOnlyList<Service>> ListAllReadOnlyAsync(Guid clientId, CancellationToken ct) =>
        (await _cache.ReadThroughAsync(CacheKeys.Services(clientId), CacheArea.Services,
            async token => await _db.Services.AsNoTracking().Where(p => p.ClientId == clientId)
                .OrderBy(p => p.Name).ToListAsync(token), ct))!;
}
