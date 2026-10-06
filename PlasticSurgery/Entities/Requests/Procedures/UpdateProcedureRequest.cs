namespace PlasticSurgery.Entities.Requests.Procedures;

/// <summary>Full replacement of the editable fields (name, code, description, duration). Active/inactive
/// is changed separately via SetProcedureActiveRequest — procedures are never deleted.</summary>
public record UpdateProcedureRequest(
    string Name,
    string? Code,
    string? Description,
    int? ConsultationDuration
);
