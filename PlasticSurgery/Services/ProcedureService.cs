using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public class ProcedureService : IProcedureService
{
    private readonly ApplicationDbContext _db;

    public ProcedureService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ProcedureResponse>> ListAsync(Guid clinicId, bool activeOnly, CancellationToken ct = default)
    {
        var query = _db.Procedures.Where(p => p.ClinicId == clinicId);
        if (activeOnly)
        {
            query = query.Where(p => p.IsActive);
        }

        var items = await query.OrderBy(p => p.Name).ToListAsync(ct);
        return items.Select(ToResponse).ToList();
    }

    public async Task<ProcedureResponse> CreateAsync(CreateProcedureRequest request, CancellationToken ct = default)
    {
        var procedure = new Procedure
        {
            Id = Guid.NewGuid(),
            ClinicId = request.ClinicId,
            Name = request.Name,
            Code = request.Code,
            Description = request.Description,
            ConsultationDuration = request.ConsultationDuration,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _db.Procedures.Add(procedure);
        await _db.SaveChangesAsync(ct);
        return ToResponse(procedure);
    }

    private static ProcedureResponse ToResponse(Procedure p) =>
        new(p.Id, p.ClinicId, p.Name, p.Code, p.Description, p.ConsultationDuration, p.IsActive);
}
