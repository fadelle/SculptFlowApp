namespace PlasticSurgery.Entities.Responses.Procedures;

public record ProcedureResponse(
    Guid Id,
    Guid ClinicId,
    string Name,
    string? Code,
    string? Description,
    int? ConsultationDuration,
    bool IsActive
);
