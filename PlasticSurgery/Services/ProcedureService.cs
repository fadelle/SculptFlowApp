using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public class ProcedureService : IProcedureService
{
    private const int MaxNameLength = 200;
    private const int MaxCodeLength = 100;
    private const int MaxDescriptionLength = 500;
    private const int MaxDurationMinutes = 480;

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

    public async Task<ProcedureResponse?> GetByIdAsync(Guid clinicId, Guid id, CancellationToken ct = default)
    {
        var procedure = await _db.Procedures.FirstOrDefaultAsync(p => p.ClinicId == clinicId && p.Id == id, ct);
        return procedure is null ? null : ToResponse(procedure);
    }

    public async Task<ProcedureResponse> CreateAsync(CreateProcedureRequest request, CancellationToken ct = default)
    {
        var (name, code, description) = Validate(request.Name, request.Code, request.Description, request.ConsultationDuration);
        await EnsureNameAvailableAsync(request.ClinicId, name, exceptId: null, ct);

        var procedure = new Procedure
        {
            Id = Guid.NewGuid(),
            ClinicId = request.ClinicId,
            Name = name,
            Code = code,
            Description = description,
            ConsultationDuration = request.ConsultationDuration,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _db.Procedures.Add(procedure);
        await _db.SaveChangesAsync(ct);
        return ToResponse(procedure);
    }

    public async Task<ProcedureResponse?> UpdateAsync(Guid clinicId, Guid id, UpdateProcedureRequest request, CancellationToken ct = default)
    {
        var procedure = await _db.Procedures.FirstOrDefaultAsync(p => p.ClinicId == clinicId && p.Id == id, ct);
        if (procedure is null) return null;

        var (name, code, description) = Validate(request.Name, request.Code, request.Description, request.ConsultationDuration);
        await EnsureNameAvailableAsync(clinicId, name, exceptId: id, ct);

        procedure.Name = name;
        procedure.Code = code;
        procedure.Description = description;
        procedure.ConsultationDuration = request.ConsultationDuration;
        procedure.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return ToResponse(procedure);
    }

    public async Task<ProcedureResponse?> SetActiveAsync(Guid clinicId, Guid id, bool isActive, CancellationToken ct = default)
    {
        var procedure = await _db.Procedures.FirstOrDefaultAsync(p => p.ClinicId == clinicId && p.Id == id, ct);
        if (procedure is null) return null;

        procedure.IsActive = isActive;
        procedure.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToResponse(procedure);
    }

    public async Task EnsureUsableAsync(Guid clinicId, Guid procedureId, CancellationToken ct = default)
    {
        var procedure = await _db.Procedures
            .Where(p => p.Id == procedureId && p.ClinicId == clinicId)
            .Select(p => new { p.Name, p.IsActive })
            .FirstOrDefaultAsync(ct);

        if (procedure is null)
        {
            throw new ArgumentException("Procedure not found for this clinic.");
        }
        if (!procedure.IsActive)
        {
            throw new ArgumentException($"Procedure '{procedure.Name}' is inactive and can't be used for new activity.");
        }
    }

    private async Task EnsureNameAvailableAsync(Guid clinicId, string name, Guid? exceptId, CancellationToken ct)
    {
        var lowered = name.ToLower();
        var exists = await _db.Procedures.AnyAsync(
            p => p.ClinicId == clinicId && p.Id != exceptId && p.Name.ToLower() == lowered, ct);
        if (exists)
        {
            throw new ArgumentException($"A procedure named '{name}' already exists.");
        }
    }

    private static (string Name, string? Code, string? Description) Validate(
        string? name, string? code, string? description, int? consultationDuration)
    {
        var trimmedName = (name ?? string.Empty).Trim();
        var trimmedCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        if (trimmedName.Length == 0) throw new ArgumentException("Name is required.");
        if (trimmedName.Length > MaxNameLength) throw new ArgumentException($"Name must be {MaxNameLength} characters or fewer.");
        if (trimmedCode is { Length: > MaxCodeLength }) throw new ArgumentException($"Code must be {MaxCodeLength} characters or fewer.");
        if (trimmedDescription is { Length: > MaxDescriptionLength })
        {
            throw new ArgumentException($"Description must be {MaxDescriptionLength} characters or fewer — put detailed information in the Knowledge Base.");
        }
        if (consultationDuration is < 1 or > MaxDurationMinutes)
        {
            throw new ArgumentException($"Consultation duration must be between 1 and {MaxDurationMinutes} minutes.");
        }

        return (trimmedName, trimmedCode, trimmedDescription);
    }

    private static ProcedureResponse ToResponse(Procedure p) =>
        new(p.Id, p.ClinicId, p.Name, p.Code, p.Description, p.ConsultationDuration, p.IsActive);
}
