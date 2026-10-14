using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Persistence.Helpers;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Procedures;

namespace PlasticSurgery.Persistence.Repositories.Procedures;

public class ProcedureRepository : IProcedureRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ICacheManager? _cache;

    public ProcedureRepository(ApplicationDbContext db, ICacheManager? cache = null)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<IReadOnlyList<Procedure>> ListAsync(Guid clinicId, bool activeOnly, CancellationToken ct = default)
    {
        var all = await ListAllReadOnlyAsync(clinicId, ct);
        return activeOnly ? all.Where(p => p.IsActive).ToList() : all;
    }

    public Task<Procedure?> GetAsync(Guid clinicId, Guid procedureId, CancellationToken ct = default) =>
        _db.Procedures.FirstOrDefaultAsync(p => p.ClinicId == clinicId && p.Id == procedureId, ct);

    public async Task<Procedure?> GetReadOnlyAsync(Guid clinicId, Guid procedureId, CancellationToken ct = default) =>
        (await ListAllReadOnlyAsync(clinicId, ct)).FirstOrDefault(p => p.Id == procedureId);

    public Task<bool> IsActiveAsync(Guid procedureId, CancellationToken ct = default) =>
        _db.Procedures.AnyAsync(p => p.Id == procedureId && p.IsActive, ct);

    public Task<bool> NameExistsAsync(Guid clinicId, string name, Guid? exceptId, CancellationToken ct = default)
    {
        var lowered = name.ToLower();
        return _db.Procedures.AnyAsync(p => p.ClinicId == clinicId && p.Id != exceptId && p.Name.ToLower() == lowered, ct);
    }

    public void Add(Procedure procedure) => _db.Procedures.Add(procedure);

    /// <summary>Every procedure of the clinic by name, read-only (cached).</summary>
    private async Task<IReadOnlyList<Procedure>> ListAllReadOnlyAsync(Guid clinicId, CancellationToken ct) =>
        (await _cache.ReadThroughAsync(CacheKeys.Procedures(clinicId), CacheArea.Procedures,
            async token => await _db.Procedures.AsNoTracking().Where(p => p.ClinicId == clinicId)
                .OrderBy(p => p.Name).ToListAsync(token), ct))!;
}
