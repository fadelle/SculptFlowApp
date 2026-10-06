using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.Appointments;

namespace PlasticSurgery.Entities.Dtos.Appointments;

/// <summary>What ScheduleAsync did (or why it did nothing). Operation: created | rescheduled | none.</summary>
public record ScheduleOutcome(
    string Operation,
    string Code,
    string Message,
    UpcomingAppointmentResponse? Appointment = null,
    UpcomingAppointmentResponse? PreviousAppointment = null,
    IReadOnlyList<UpcomingAppointmentResponse>? Existing = null,
    string? RequestedLabel = null
);
