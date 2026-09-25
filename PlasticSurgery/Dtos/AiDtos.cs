namespace PlasticSurgery.Dtos;

/// <summary>Response for GET /api/ai/clinic-info — general info the AI agent answers questions
/// from directly instead of inventing it. Address/OperatingHours/ConsultationInfo are free text
/// (see Clinic entity), matching this MVP's level of structure elsewhere.</summary>
public record ClinicInfoResponse(
    Guid ClinicId,
    string Name,
    string? Phone,
    string? Email,
    string? Website,
    string? Address,
    string? OperatingHours,
    string? ConsultationInfo,
    string Timezone
);

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

public record RescheduleConsultationRequest(
    DateTimeOffset ScheduledStart,
    [property: System.Text.Json.Serialization.JsonConverter(typeof(LenientNullableDateTimeOffsetConverter))] DateTimeOffset? ScheduledEnd,
    string? Reason);

public record CancelConsultationRequest(string? Reason);

public record HandoffToHumanRequest(string? Reason);
