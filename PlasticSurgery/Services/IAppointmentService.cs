using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public interface IAppointmentService
{
    /// <summary>Naive fixed-hour slot generator (9am-5pm, clinic timezone) for the next `days` days,
    /// minus slots that overlap an existing non-canceled appointment. Replace with real calendar
    /// integration (Google Calendar/Calendly) when Project 6's future additions are built.</summary>
    Task<IReadOnlyList<AvailableSlotResponse>> GetAvailableSlotsAsync(Guid clinicId, int days, CancellationToken ct = default);

    Task<AppointmentResponse> CreateAsync(CreateAppointmentRequest request, CancellationToken ct = default);

    Task<AppointmentResponse?> GetByIdAsync(Guid clinicId, Guid id, CancellationToken ct = default);

    Task<AppointmentResponse?> UpdateStatusAsync(Guid clinicId, Guid id, string status, CancellationToken ct = default);

    /// <summary>Changes an appointment's scheduled time (a real reschedule, not just a status label)
    /// and resets its status back to Booked — a rescheduled appointment isn't "confirmed" until
    /// staff/the clinic confirms the new time again. Used by the AI agent's reschedule_consultation
    /// tool — see Controllers/AiController.cs.</summary>
    Task<AppointmentResponse?> RescheduleAsync(
        Guid clinicId, Guid id, DateTimeOffset newStart, DateTimeOffset? newEnd, string? reason, CancellationToken ct = default);

    /// <summary>Cancels an appointment — thin wrapper over UpdateStatusAsync that also records the
    /// reason and logs a dedicated event, for the AI agent's cancel_consultation tool.</summary>
    Task<AppointmentResponse?> CancelAsync(Guid clinicId, Guid id, string? reason, CancellationToken ct = default);

    Task<(IReadOnlyList<AppointmentResponse> Items, int TotalCount)> ListAsync(
        Guid clinicId, string? status, DateTimeOffset? from, DateTimeOffset? to, int skip, int take, CancellationToken ct = default);

    /// <summary>Every appointment in the visible month calendar grid for the Appointments page — the grid's
    /// leading/trailing days from adjacent months included, everything grouped by LOCAL calendar day in the
    /// clinic's own timezone (never UTC, never the browser's timezone). No status filter: booked, confirmed,
    /// canceled and completed appointments all appear, same as the rest of the app.</summary>
    Task<CalendarMonthResponse> GetCalendarMonthAsync(Guid clinicId, int year, int month, CancellationToken ct = default);
}
