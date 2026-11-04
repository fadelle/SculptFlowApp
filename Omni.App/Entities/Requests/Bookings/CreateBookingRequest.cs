namespace Omni.App.Entities.Requests.Bookings;

public record CreateBookingRequest(
    Guid ClientId,
    Guid ContactId,
    Guid? ServiceId,
    string BookingType,
    DateTimeOffset ScheduledStart,
    DateTimeOffset? ScheduledEnd,
    string? LocationType,
    string? LocationName,
    string? Notes,
    // A member books past the soft rules (opening hours, notice, horizon, past) after being asked (QA t5-ops-01).
    // Overlaps are always refused; the AI never sets it.
    bool OverrideRules = false
);
