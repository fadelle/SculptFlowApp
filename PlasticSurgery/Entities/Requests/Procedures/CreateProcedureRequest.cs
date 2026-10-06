namespace PlasticSurgery.Entities.Requests.Procedures;

public record CreateProcedureRequest(
    Guid ClinicId,
    string Name,
    string? Code,
    string? Description,
    int? ConsultationDuration
);
