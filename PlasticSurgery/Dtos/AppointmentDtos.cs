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


/// <summary>One appointment as shown on the month calendar — the same appointments table as everywhere else,
/// with LocalDate/LocalTime PRE-COMPUTED server-side in the clinic's own timezone (Clinic.Timezone), so the
/// browser never converts time zones itself and can't mis-group an appointment into the wrong day.</summary>
public record CalendarAppointmentResponse(
    Guid Id,
    Guid LeadId,
    string? LeadFullName,
    Guid? ProcedureId,
    string? ProcedureName,
    string Status,
    DateTimeOffset ScheduledStart,
    DateTimeOffset? ScheduledEnd,
    /// <summary>"yyyy-MM-dd" in the clinic's timezone — the calendar day this appointment belongs on.</summary>
    string LocalDate,
    /// <summary>"HH:mm" in the clinic's timezone.</summary>
    string LocalTime
);

/// <summary>GET /api/appointments/calendar?year=&month= — every appointment in the visible month GRID (which
/// includes a few leading/trailing days from the adjacent months to fill whole weeks), clinic-scoped. One call
/// covers both the month grid and the day drawer; clicking a day never needs a second request.</summary>
public record CalendarMonthResponse(
    int Year,
    int Month,
    string Timezone,
    /// <summary>The grid's first/last visible day (inclusive), "yyyy-MM-dd" — may fall in the previous/next month.</summary>
    string GridStart,
    string GridEnd,
    IReadOnlyList<CalendarAppointmentResponse> Items,
    /// <summary>Clinic-wide count of past appointments still booked/confirmed — nobody recorded an outcome (attended / no-show / canceled).</summary>
    int NeedsOutcomeCount
);

/// <summary>One upcoming appointment as the AI describes it to a patient — Date/Time/Label are clinic-local plain text (same
/// convention as available slots); Start/End keep the clinic's UTC offset.</summary>
public record UpcomingAppointmentResponse(
    Guid Id,
    string Status,
    Guid? ProcedureId,
    string? ProcedureName,
    DateTimeOffset ScheduledStart,
    DateTimeOffset? ScheduledEnd,
    string Date,
    string Time,
    string Label
);

public record UpcomingAppointmentsResponse(string Timezone, IReadOnlyList<UpcomingAppointmentResponse> Appointments);
