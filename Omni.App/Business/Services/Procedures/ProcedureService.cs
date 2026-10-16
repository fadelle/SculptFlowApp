using Omni.App.Business.Contracts.Services.Procedures;
using Omni.App.Business.Mappers.Procedures;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Procedures;
using Omni.App.Entities.Responses.Procedures;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Procedures;

namespace Omni.App.Business.Services.Procedures;

public class ProcedureService : IProcedureService
{
    private const int MaxNameLength = 200;
    private const int MaxCodeLength = 100;
    private const int MaxDescriptionLength = 500;
    private const int MaxDurationMinutes = 480;

    private readonly IProcedureRepository _procedures;
    private readonly IUnitOfWork _unitOfWork;

    public ProcedureService(IProcedureRepository procedures, IUnitOfWork unitOfWork)
    {
        _procedures = procedures;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<ProcedureResponse>> ListAsync(Guid clientId, bool activeOnly, CancellationToken ct = default)
    {
        var items = await _procedures.ListAsync(clientId, activeOnly, ct);
        return items.Select(ProcedureMapper.ToResponse).ToList();
    }

    public async Task<ProcedureResponse?> GetByIdAsync(Guid clientId, Guid id, CancellationToken ct = default)
    {
        var procedure = await _procedures.GetAsync(clientId, id, ct);
        return procedure is null ? null : ProcedureMapper.ToResponse(procedure);
    }

    public async Task<ProcedureResponse> CreateAsync(CreateProcedureRequest request, CancellationToken ct = default)
    {
        var (name, code, description) = Validate(request.Name, request.Code, request.Description, request.ConsultationDuration);
        await EnsureNameAvailableAsync(request.ClientId, name, exceptId: null, ct);

        var procedure = new Procedure
        {
            Id = Guid.NewGuid(),
            ClientId = request.ClientId,
            Name = name,
            Code = code,
            Description = description,
            ConsultationDuration = request.ConsultationDuration,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _procedures.Add(procedure);
        await _unitOfWork.SaveChangesAsync(ct);
        return ProcedureMapper.ToResponse(procedure);
    }

    public async Task<ProcedureResponse?> UpdateAsync(Guid clientId, Guid id, UpdateProcedureRequest request, CancellationToken ct = default)
    {
        var procedure = await _procedures.GetAsync(clientId, id, ct);
        if (procedure is null) return null;

        var (name, code, description) = Validate(request.Name, request.Code, request.Description, request.ConsultationDuration);
        await EnsureNameAvailableAsync(clientId, name, exceptId: id, ct);

        procedure.Name = name;
        procedure.Code = code;
        procedure.Description = description;
        procedure.ConsultationDuration = request.ConsultationDuration;
        procedure.UpdatedAt = DateTimeOffset.UtcNow;

        await _unitOfWork.SaveChangesAsync(ct);
        return ProcedureMapper.ToResponse(procedure);
    }

    public async Task<ProcedureResponse?> SetActiveAsync(Guid clientId, Guid id, bool isActive, CancellationToken ct = default)
    {
        var procedure = await _procedures.GetAsync(clientId, id, ct);
        if (procedure is null) return null;

        procedure.IsActive = isActive;
        procedure.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);
        return ProcedureMapper.ToResponse(procedure);
    }

    public async Task EnsureUsableAsync(Guid clientId, Guid procedureId, CancellationToken ct = default)
    {
        var procedure = await _procedures.GetReadOnlyAsync(clientId, procedureId, ct);

        if (procedure is null)
        {
            throw new ArgumentException("Procedure not found for this client.");
        }
        if (!procedure.IsActive)
        {
            throw new ArgumentException($"Procedure '{procedure.Name}' is inactive and can't be used for new activity.");
        }
    }

    private async Task EnsureNameAvailableAsync(Guid clientId, string name, Guid? exceptId, CancellationToken ct)
    {
        if (await _procedures.NameExistsAsync(clientId, name, exceptId, ct))
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
}
