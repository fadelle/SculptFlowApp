namespace Omni.App.Entities.Responses.Appointments;

/// <summary>One appointment as shown on the month calendar — the same appointments table as everywhere else,
/// with LocalDate/LocalTime PRE-COMPUTED server-side in the display timezone (CalendarMonthResponse.Timezone — the
/// client's, or the viewer's on "My time"), so the browser can't mis-group an appointment into the wrong day.</summary>
public record CalendarAppointmentResponse(
    Guid Id,
    Guid LeadId,
    string? LeadFullName,
    Guid? ServiceId,
    string? ServiceName,
    string Status,
    DateTimeOffset ScheduledStart,
    DateTimeOffset? ScheduledEnd,
    /// <summary>"yyyy-MM-dd" in the display timezone — the calendar day this appointment belongs on.</summary>
    string LocalDate,
    /// <summary>"HH:mm" in the display timezone.</summary>
    string LocalTime
);
