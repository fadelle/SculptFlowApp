using PlasticSurgery.Common.Converters;

namespace PlasticSurgery.Entities.Requests.Ai;

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
