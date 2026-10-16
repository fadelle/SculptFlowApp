using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Bookings;

namespace Omni.App.Entities.Dtos.Bookings;

/// <summary>What CancelBookingAsync did. Operation: canceled | none.</summary>
public record CancelOutcome(
    string Operation,
    string Code,
    string Message,
    UpcomingBookingResponse? Booking = null,
    IReadOnlyList<UpcomingBookingResponse>? Existing = null
);
