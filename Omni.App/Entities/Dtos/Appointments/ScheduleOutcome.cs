using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Appointments;

namespace Omni.App.Entities.Dtos.Appointments;

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
