namespace PlasticSurgery.Entities.Responses.Appointments;

/// <summary>One upcoming appointment as the AI describes it to a patient — Date/Time/Label are clinic-local plain text (same
/// convention as available slots); Start/End keep the clinic's UTC offset.</summary>
public record UpcomingAppointmentResponse(
    Guid Id,
    string Status,
    string AppointmentType,
    Guid? ProcedureId,
    string? ProcedureName,
    DateTimeOffset ScheduledStart,
    DateTimeOffset? ScheduledEnd,
    string Date,
    string Time,
    string Label
);
