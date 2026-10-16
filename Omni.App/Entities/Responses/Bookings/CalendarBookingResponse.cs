namespace Omni.App.Entities.Responses.Bookings;

/// <summary>One booking as shown on the month calendar — the same bookings table as everywhere else,
/// with LocalDate/LocalTime PRE-COMPUTED server-side in the display timezone (CalendarMonthResponse.Timezone — the
/// client's, or the viewer's on "My time"), so the browser can't mis-group a booking into the wrong day.</summary>
public record CalendarBookingResponse(
    Guid Id,
    Guid LeadId,
    string? LeadFullName,
    Guid? ServiceId,
    string? ServiceName,
    string Status,
    DateTimeOffset ScheduledStart,
    DateTimeOffset? ScheduledEnd,
    /// <summary>"yyyy-MM-dd" in the display timezone — the calendar day this booking belongs on.</summary>
    string LocalDate,
    /// <summary>"HH:mm" in the display timezone.</summary>
    string LocalTime
);
