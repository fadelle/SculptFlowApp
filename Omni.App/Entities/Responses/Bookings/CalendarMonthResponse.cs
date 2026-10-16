namespace Omni.App.Entities.Responses.Bookings;

/// <summary>GET /api/bookings/calendar?year=&month= — every booking in the visible month GRID (which
/// includes a few leading/trailing days from the adjacent months to fill whole weeks), client-scoped. One call
/// covers both the month grid and the day drawer; clicking a day never needs a second request.</summary>
public record CalendarMonthResponse(
    int Year,
    int Month,
    /// <summary>The timezone LocalDate/LocalTime and the grid are in (the client's, or the viewer's on "My time").</summary>
    string Timezone,
    string ClientTimezone,
    /// <summary>The grid's first/last visible day (inclusive), "yyyy-MM-dd" — may fall in the previous/next month.</summary>
    string GridStart,
    string GridEnd,
    IReadOnlyList<CalendarBookingResponse> Items,
    /// <summary>Client-wide count of past bookings still booked/confirmed — nobody recorded an outcome (attended / no-show / canceled).</summary>
    int NeedsOutcomeCount
);
