using Omni.App.Common.Converters;

namespace Omni.App.Entities.Requests.Ai;

public record RescheduleBookingRequest(
    DateTimeOffset ScheduledStart,
    [property: System.Text.Json.Serialization.JsonConverter(typeof(LenientNullableDateTimeOffsetConverter))] DateTimeOffset? ScheduledEnd,
    string? Reason);
