using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.Appointments;

namespace PlasticSurgery.Entities.Dtos.Ai;

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
