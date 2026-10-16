using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Bookings;

namespace Omni.App.Entities.Dtos.Ai;

/// <summary>What book_slot returns for EVERY business outcome, always as HTTP 200 — n8n hands a normal result to the AI far more
/// reliably than a generic error string, and Success/Code make it unmistakable whether anything was booked. Only malformed requests
/// (400) and a bad ingest key (401) use error statuses.</summary>
public record BookSlotResult(
    bool Success,
    /// <summary>BOOKED, EXISTING_UPCOMING_BOOKING, SLOT_UNAVAILABLE, CONTACT_NOT_FOUND or INVALID_REQUEST.</summary>
    string Code,
    string Message,
    /// <summary>On failure: what the AI must (not) say and do next.</summary>
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Instruction = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] UpcomingBookingResponse? Booking = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] UpcomingBookingResponse? ExistingBooking = null
);
