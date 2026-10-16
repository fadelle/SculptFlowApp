namespace Omni.App.Entities.Responses.Appointments;

public record AppointmentResponse(
    Guid Id,
    Guid ClientId,
    Guid LeadId,
    string? LeadFullName,
    Guid? ServiceId,
    string? ServiceName,
    string AppointmentType,
    string Status,
    DateTimeOffset ScheduledStart,
    DateTimeOffset? ScheduledEnd,
    string? LocationType,
    string? LocationName,
    string? Notes,
    DateTimeOffset CreatedAt
);
