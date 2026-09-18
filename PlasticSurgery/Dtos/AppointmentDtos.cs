namespace PlasticSurgery.Dtos;

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

public record UpdateAppointmentStatusRequest(string Status);

/// <summary>A naive fixed-slot day view for /api/appointments/available. Replace with real
/// calendar-integration logic (Google Calendar/Calendly) in a later project.</summary>
public record AvailableSlotResponse(DateTimeOffset Start, DateTimeOffset End);
