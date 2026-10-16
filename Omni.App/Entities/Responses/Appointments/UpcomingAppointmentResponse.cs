namespace Omni.App.Entities.Responses.Appointments;

/// <summary>One upcoming appointment as the AI describes it to a patient — Date/Time/Label are client-local plain text (same
/// convention as available slots); Start/End keep the client's UTC offset.</summary>
public record UpcomingAppointmentResponse(
    Guid Id,
    string Status,
    string AppointmentType,
    Guid? ServiceId,
    string? ServiceName,
    DateTimeOffset ScheduledStart,
    DateTimeOffset? ScheduledEnd,
    string Date,
    string Time,
    string Label
);
