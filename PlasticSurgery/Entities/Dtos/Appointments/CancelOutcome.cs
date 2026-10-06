using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.Appointments;

namespace PlasticSurgery.Entities.Dtos.Appointments;

/// <summary>What CancelConsultationAsync did. Operation: canceled | none.</summary>
public record CancelOutcome(
    string Operation,
    string Code,
    string Message,
    UpcomingAppointmentResponse? Appointment = null,
    IReadOnlyList<UpcomingAppointmentResponse>? Existing = null
);
