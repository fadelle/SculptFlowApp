namespace PlasticSurgery.Entities.Responses.Appointments;

public record AppointmentResponse(
    Guid Id,
    Guid ClinicId,
    Guid LeadId,
    string? LeadFullName,
    Guid? ProcedureId,
    string? ProcedureName,
    string AppointmentType,
    string Status,
    DateTimeOffset ScheduledStart,
    DateTimeOffset? ScheduledEnd,
    string? LocationType,
    string? LocationName,
    string? Notes,
    DateTimeOffset CreatedAt
);
