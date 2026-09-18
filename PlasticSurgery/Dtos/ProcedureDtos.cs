namespace PlasticSurgery.Dtos;

public record ProcedureResponse(
    Guid Id,
    Guid ClinicId,
    string Name,
    string? Code,
    string? Description,
    int? ConsultationDuration,
    bool IsActive
);

public record CreateProcedureRequest(
    Guid ClinicId,
    string Name,
    string? Code,
    string? Description,
    int? ConsultationDuration
);
