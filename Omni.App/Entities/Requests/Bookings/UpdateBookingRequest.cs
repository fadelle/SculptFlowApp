namespace Omni.App.Entities.Requests.Bookings;

/// <summary>A member's edit of a booking (PATCH /api/bookings/{id}, QA t5-ops-04): the whole editable set, as the booking dialog
/// shows it. Null service, end, location or notes = none.</summary>
public record UpdateBookingRequest(
    DateTimeOffset ScheduledStart,
    DateTimeOffset? ScheduledEnd,
    Guid? ServiceId,
    string? LocationType,
    string? Notes
);
