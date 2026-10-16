namespace Omni.App.Entities.Requests.Appointments;

public record CreateAppointmentRequest(
    Guid ClientId,
    Guid LeadId,
    Guid? ServiceId,
    string AppointmentType,
    DateTimeOffset ScheduledStart,
    DateTimeOffset? ScheduledEnd,
    string? LocationType,
    string? LocationName,
    string? Notes
);
