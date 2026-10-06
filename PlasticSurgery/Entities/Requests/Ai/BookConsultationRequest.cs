using PlasticSurgery.Common.Converters;

namespace PlasticSurgery.Entities.Requests.Ai;

/// <summary>Body for POST /api/ai/appointments/book — same shape as CreateAppointmentRequest minus
/// ClinicId, which the AI controller takes from the query string like every other AI endpoint.</summary>
public record BookConsultationRequest(
    Guid LeadId,
    [property: System.Text.Json.Serialization.JsonConverter(typeof(LenientNullableGuidConverter))] Guid? ProcedureId,
    string? AppointmentType,
    DateTimeOffset ScheduledStart,
    [property: System.Text.Json.Serialization.JsonConverter(typeof(LenientNullableDateTimeOffsetConverter))] DateTimeOffset? ScheduledEnd,
    string? LocationType,
    string? LocationName,
    string? Notes
);
