namespace Omni.App.Entities.Requests.Procedures;

public record CreateProcedureRequest(
    Guid ClientId,
    string Name,
    string? Code,
    string? Description,
    int? ConsultationDuration
);
