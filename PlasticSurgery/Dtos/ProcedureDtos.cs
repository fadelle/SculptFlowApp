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

/// <summary>Full replacement of the editable fields (name, code, description, duration). Active/inactive
/// is changed separately via SetProcedureActiveRequest — procedures are never deleted.</summary>
public record UpdateProcedureRequest(
    string Name,
    string? Code,
    string? Description,
    int? ConsultationDuration
);

public record SetProcedureActiveRequest(bool IsActive);
