using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Bookings;

namespace Omni.App.Entities.Dtos.Ai;

/// <summary>Result of schedule_booking — always HTTP 200 for business outcomes. Operation is what actually happened:
/// "created", "rescheduled" or "none". Only Success = true with Operation created/rescheduled means the patient's booking changed.</summary>
public record ScheduleBookingResult(
    bool Success,
    /// <summary>created | rescheduled | none</summary>
    string Operation,
    /// <summary>BOOKED, RESCHEDULED, CONFIRMATION_REQUIRED, MULTIPLE_UPCOMING_BOOKINGS, SLOT_UNAVAILABLE, LEAD_NOT_FOUND or INVALID_REQUEST.</summary>
    string Code,
    string Message,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Instruction = null,
    /// <summary>The patient's booking after a successful create/reschedule, in client-local time.</summary>
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] UpcomingBookingResponse? Booking = null,
    /// <summary>After a reschedule: the booking as it was before the move.</summary>
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] UpcomingBookingResponse? PreviousBooking = null,
    /// <summary>When no change was made because of an existing booking: what the patient already has.</summary>
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<UpcomingBookingResponse>? ExistingUpcomingBookings = null,
    /// <summary>The time the patient asked for, in client-local wording (e.g. "Tuesday, Sep 29 at 4:00 PM").</summary>
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? RequestedLabel = null
);
