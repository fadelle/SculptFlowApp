using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Procedures;

namespace PlasticSurgery.Persistence.Repositories.Procedures;

public class ProcedureRepository : IProcedureRepository
{
    private readonly ApplicationDbContext _db;

    public ProcedureRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Procedure>> ListAsync(Guid clinicId, bool activeOnly, CancellationToken ct = default)
    {
        var query = _db.Procedures.Where(p => p.ClinicId == clinicId);
        if (activeOnly)
        {
            query = query.Where(p => p.IsActive);
        }

        return await query.OrderBy(p => p.Name).ToListAsync(ct);
    }

    public Task<Procedure?> GetAsync(Guid clinicId, Guid procedureId, CancellationToken ct = default) =>
        _db.Procedures.FirstOrDefaultAsync(p => p.ClinicId == clinicId && p.Id == procedureId, ct);

    public Task<Procedure?> GetReadOnlyAsync(Guid clinicId, Guid procedureId, CancellationToken ct = default) =>
        _db.Procedures.AsNoTracking().FirstOrDefaultAsync(p => p.ClinicId == clinicId && p.Id == procedureId, ct);

    public Task<bool> IsActiveAsync(Guid procedureId, CancellationToken ct = default) =>
        _db.Procedures.AnyAsync(p => p.Id == procedureId && p.IsActive, ct);

    public Task<bool> NameExistsAsync(Guid clinicId, string name, Guid? exceptId, CancellationToken ct = default)
    {
        var lowered = name.ToLower();
        return _db.Procedures.AnyAsync(p => p.ClinicId == clinicId && p.Id != exceptId && p.Name.ToLower() == lowered, ct);
    }

    public void Add(Procedure procedure) => _db.Procedures.Add(procedure);
}
