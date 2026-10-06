using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.Appointments;

namespace PlasticSurgery.Entities.Dtos.Ai;

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
