using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Appointments;

namespace Omni.App.Entities.Dtos.Appointments;

/// <summary>What CancelConsultationAsync did. Operation: canceled | none.</summary>
public record CancelOutcome(
    string Operation,
    string Code,
    string Message,
    UpcomingAppointmentResponse? Appointment = null,
    IReadOnlyList<UpcomingAppointmentResponse>? Existing = null
);
