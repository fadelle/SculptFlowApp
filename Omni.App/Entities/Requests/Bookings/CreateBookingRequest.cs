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
    string? Notes
);
