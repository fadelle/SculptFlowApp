using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.Appointments;

namespace PlasticSurgery.Entities.Dtos.Ai;

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
