namespace PlasticSurgery.Entities.Responses.Appointments;

/// <summary>GET /api/appointments/calendar?year=&month= — every appointment in the visible month GRID (which
/// includes a few leading/trailing days from the adjacent months to fill whole weeks), clinic-scoped. One call
/// covers both the month grid and the day drawer; clicking a day never needs a second request.</summary>
public record CalendarMonthResponse(
    int Year,
    int Month,
    /// <summary>The timezone LocalDate/LocalTime and the grid are in (the clinic's, or the viewer's on "My time").</summary>
    string Timezone,
    string ClinicTimezone,
    /// <summary>The grid's first/last visible day (inclusive), "yyyy-MM-dd" — may fall in the previous/next month.</summary>
    string GridStart,
    string GridEnd,
    IReadOnlyList<CalendarAppointmentResponse> Items,
    /// <summary>Clinic-wide count of past appointments still booked/confirmed — nobody recorded an outcome (attended / no-show / canceled).</summary>
    int NeedsOutcomeCount
);
