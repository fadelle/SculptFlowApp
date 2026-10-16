using Omni.App.Common.Converters;

namespace Omni.App.Entities.Requests.Ai;

/// <summary>Body for POST /api/ai/bookings/book — same shape as CreateBookingRequest minus
/// ClientId, which the AI controller takes from the query string like every other AI endpoint.</summary>
public record BookSlotRequest(
    Guid ContactId,
    [property: System.Text.Json.Serialization.JsonConverter(typeof(LenientNullableGuidConverter))] Guid? ServiceId,
    string? BookingType,
    DateTimeOffset ScheduledStart,
    [property: System.Text.Json.Serialization.JsonConverter(typeof(LenientNullableDateTimeOffsetConverter))] DateTimeOffset? ScheduledEnd,
    string? LocationType,
    string? LocationName,
    string? Notes
);
