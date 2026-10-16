using Microsoft.EntityFrameworkCore;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Persistence.Helpers;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Procedures;

namespace Omni.App.Persistence.Repositories.Procedures;

public class ProcedureRepository : IProcedureRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ICacheManager? _cache;

    public ProcedureRepository(ApplicationDbContext db, ICacheManager? cache = null)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<IReadOnlyList<Procedure>> ListAsync(Guid clientId, bool activeOnly, CancellationToken ct = default)
    {
        var all = await ListAllReadOnlyAsync(clientId, ct);
        return activeOnly ? all.Where(p => p.IsActive).ToList() : all;
    }

    public Task<Procedure?> GetAsync(Guid clientId, Guid procedureId, CancellationToken ct = default) =>
        _db.Procedures.FirstOrDefaultAsync(p => p.ClientId == clientId && p.Id == procedureId, ct);

    public async Task<Procedure?> GetReadOnlyAsync(Guid clientId, Guid procedureId, CancellationToken ct = default) =>
        (await ListAllReadOnlyAsync(clientId, ct)).FirstOrDefault(p => p.Id == procedureId);

    public Task<bool> IsActiveAsync(Guid procedureId, CancellationToken ct = default) =>
        _db.Procedures.AnyAsync(p => p.Id == procedureId && p.IsActive, ct);

    public Task<bool> NameExistsAsync(Guid clientId, string name, Guid? exceptId, CancellationToken ct = default)
    {
        var lowered = name.ToLower();
        return _db.Procedures.AnyAsync(p => p.ClientId == clientId && p.Id != exceptId && p.Name.ToLower() == lowered, ct);
    }

    public void Add(Procedure procedure) => _db.Procedures.Add(procedure);

    /// <summary>Every procedure of the client by name, read-only (cached).</summary>
    private async Task<IReadOnlyList<Procedure>> ListAllReadOnlyAsync(Guid clientId, CancellationToken ct) =>
        (await _cache.ReadThroughAsync(CacheKeys.Procedures(clientId), CacheArea.Procedures,
            async token => await _db.Procedures.AsNoTracking().Where(p => p.ClientId == clientId)
                .OrderBy(p => p.Name).ToListAsync(token), ct))!;
}
