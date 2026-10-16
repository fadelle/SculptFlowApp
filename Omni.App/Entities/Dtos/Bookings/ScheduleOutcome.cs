using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Bookings;

namespace Omni.App.Entities.Dtos.Bookings;

/// <summary>What ScheduleAsync did (or why it did nothing). Operation: created | rescheduled | none.</summary>
public record ScheduleOutcome(
    string Operation,
    string Code,
    string Message,
    UpcomingBookingResponse? Booking = null,
    UpcomingBookingResponse? PreviousBooking = null,
    IReadOnlyList<UpcomingBookingResponse>? Existing = null,
    string? RequestedLabel = null
);
