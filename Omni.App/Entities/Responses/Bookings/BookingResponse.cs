namespace Omni.App.Entities.Responses.Bookings;

public record BookingResponse(
    Guid Id,
    Guid ClientId,
    Guid LeadId,
    string? LeadFullName,
    Guid? ServiceId,
    string? ServiceName,
    string BookingType,
    string Status,
    DateTimeOffset ScheduledStart,
    DateTimeOffset? ScheduledEnd,
    string? LocationType,
    string? LocationName,
    string? Notes,
    DateTimeOffset CreatedAt
);
