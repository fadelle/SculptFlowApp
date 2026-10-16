using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Bookings;

namespace Omni.App.Entities.Dtos.Ai;

/// <summary>Result of cancel_booking — always HTTP 200 for business outcomes. Only Success = true (Operation "canceled") means an
/// booking is canceled; every failure carries an Instruction saying nothing was canceled and what to do next.</summary>
public record CancelBookingResult(
    bool Success,
    /// <summary>canceled | none</summary>
    string Operation,
    /// <summary>CANCELED, NO_UPCOMING_BOOKING, MULTIPLE_UPCOMING_BOOKINGS or INVALID_REQUEST.</summary>
    string Code,
    string Message,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Instruction = null,
    /// <summary>The canceled booking, in client-local time.</summary>
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] UpcomingBookingResponse? Booking = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<UpcomingBookingResponse>? ExistingUpcomingBookings = null
);
