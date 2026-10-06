using PlasticSurgery.Common.Converters;

namespace PlasticSurgery.Entities.Requests.Ai;

public record RescheduleConsultationRequest(
    DateTimeOffset ScheduledStart,
    [property: System.Text.Json.Serialization.JsonConverter(typeof(LenientNullableDateTimeOffsetConverter))] DateTimeOffset? ScheduledEnd,
    string? Reason);
