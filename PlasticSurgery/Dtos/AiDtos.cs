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

/// <summary>What book_consultation returns for EVERY business outcome, always as HTTP 200 — n8n hands a normal result to the AI far more
/// reliably than a generic error string, and Success/Code make it unmistakable whether anything was booked. Only malformed requests
/// (400) and a bad ingest key (401) use error statuses.</summary>
public record BookConsultationResult(
    bool Success,
    /// <summary>BOOKED, EXISTING_UPCOMING_APPOINTMENT, SLOT_UNAVAILABLE, LEAD_NOT_FOUND or INVALID_REQUEST.</summary>
    string Code,
    string Message,
    /// <summary>On failure: what the AI must (not) say and do next.</summary>
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Instruction = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] UpcomingAppointmentResponse? Appointment = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] UpcomingAppointmentResponse? ExistingAppointment = null
);

/// <summary>Body for POST /api/ai/appointments/schedule — the ONE mutation the AI uses for booking and moving a consultation.
/// clinicId and leadId are query parameters set by the workflow, never by the AI. The backend decides create vs. reschedule from the
/// real appointment state; the AI only supplies the target time and whether the patient clearly agreed to replace an existing appointment.</summary>
public record ScheduleConsultationRequest(
    /// <summary>The exact "start" of the chosen slot from get_available_slots.</summary>
    DateTimeOffset ScheduledStart,
    [property: System.Text.Json.Serialization.JsonConverter(typeof(LenientNullableGuidConverter))] Guid? ProcedureId = null,
    string? AppointmentType = null,
    string? Notes = null,
    /// <summary>Why the patient is changing an existing appointment (only used when it is moved).</summary>
    string? Reason = null,
    /// <summary>Set true ONLY after the patient clearly agreed to move the appointment they already have. Ignored when they have none.</summary>
    [property: System.Text.Json.Serialization.JsonConverter(typeof(LenientBoolConverter))] bool ConfirmReplaceExisting = false,
    /// <summary>Only needed when the patient has several upcoming appointments and must pick which one to move.</summary>
    [property: System.Text.Json.Serialization.JsonConverter(typeof(LenientNullableGuidConverter))] Guid? AppointmentId = null
);

/// <summary>Result of schedule_consultation — always HTTP 200 for business outcomes. Operation is what actually happened:
/// "created", "rescheduled" or "none". Only Success = true with Operation created/rescheduled means the patient's appointment changed.</summary>
public record ScheduleConsultationResult(
    bool Success,
    /// <summary>created | rescheduled | none</summary>
    string Operation,
    /// <summary>BOOKED, RESCHEDULED, CONFIRMATION_REQUIRED, MULTIPLE_UPCOMING_APPOINTMENTS, SLOT_UNAVAILABLE, LEAD_NOT_FOUND or INVALID_REQUEST.</summary>
    string Code,
    string Message,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Instruction = null,
    /// <summary>The patient's appointment after a successful create/reschedule, in clinic-local time.</summary>
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] UpcomingAppointmentResponse? Appointment = null,
    /// <summary>After a reschedule: the appointment as it was before the move.</summary>
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] UpcomingAppointmentResponse? PreviousAppointment = null,
    /// <summary>When no change was made because of an existing appointment: what the patient already has.</summary>
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<UpcomingAppointmentResponse>? ExistingUpcomingAppointments = null,
    /// <summary>The time the patient asked for, in clinic-local wording (e.g. "Tuesday, Sep 29 at 4:00 PM").</summary>
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? RequestedLabel = null
);

/// <summary>Result of cancel_consultation — always HTTP 200 for business outcomes. Only Success = true (Operation "canceled") means an
/// appointment is canceled; every failure carries an Instruction saying nothing was canceled and what to do next.</summary>
public record CancelConsultationResult(
    bool Success,
    /// <summary>canceled | none</summary>
    string Operation,
    /// <summary>CANCELED, NO_UPCOMING_APPOINTMENT, MULTIPLE_UPCOMING_APPOINTMENTS or INVALID_REQUEST.</summary>
    string Code,
    string Message,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Instruction = null,
    /// <summary>The canceled appointment, in clinic-local time.</summary>
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] UpcomingAppointmentResponse? Appointment = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<UpcomingAppointmentResponse>? ExistingUpcomingAppointments = null
);
