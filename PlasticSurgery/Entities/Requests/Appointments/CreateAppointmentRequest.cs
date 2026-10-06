namespace PlasticSurgery.Entities.Requests.Appointments;

public record CreateAppointmentRequest(
    Guid ClinicId,
    Guid LeadId,
    Guid? ProcedureId,
    string AppointmentType,
    DateTimeOffset ScheduledStart,
    DateTimeOffset? ScheduledEnd,
    string? LocationType,
    string? LocationName,
    string? Notes
);
