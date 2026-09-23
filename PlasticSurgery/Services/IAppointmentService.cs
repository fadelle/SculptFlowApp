using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public interface IAppointmentService
{
    /// <summary>Books through the same insert as CreateAsync, but first re-checks — under a per-clinic lock, so two
    /// simultaneous attempts for one slot cannot both succeed — that the slot is still bookable per IAvailabilityService
    /// (weekly schedule, exceptions, notice, horizon, existing appointments, buffer). Fills in a missing end time from the
    /// procedure/default duration. Throws SlotUnavailableException when it is not. Used by the AI's book_consultation.</summary>
    Task<AppointmentResponse> BookAvailableSlotAsync(CreateAppointmentRequest request, CancellationToken ct = default);

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

/// <summary>The requested slot is not (or is no longer) bookable; Message is safe to show the patient/AI.</summary>
public class SlotUnavailableException : Exception
{
    public SlotUnavailableException(string message) : base(message) { }
}
