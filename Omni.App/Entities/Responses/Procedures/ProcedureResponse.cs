namespace Omni.App.Entities.Responses.Procedures;

public record ProcedureResponse(
    Guid Id,
    Guid ClientId,
    string Name,
    string? Code,
    string? Description,
    int? ConsultationDuration,
    bool IsActive
);
