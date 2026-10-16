namespace Omni.App.Entities.Responses.Bookings;

/// <summary>One upcoming booking as the AI describes it to a contact — Date/Time/Label are client-local plain text (same
/// convention as available slots); Start/End keep the client's UTC offset.</summary>
public record UpcomingBookingResponse(
    Guid Id,
    string Status,
    string BookingType,
    Guid? ServiceId,
    string? ServiceName,
    DateTimeOffset ScheduledStart,
    DateTimeOffset? ScheduledEnd,
    string Date,
    string Time,
    string Label
);
