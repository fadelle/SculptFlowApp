using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public interface IAppointmentService
{
    /// <summary>Books through the same insert as CreateAsync, but first re-checks — under a per-clinic lock, so two
    /// simultaneous attempts for one slot cannot both succeed — that the slot is still bookable per IAvailabilityService
    /// (weekly schedule, exceptions, notice, horizon, existing appointments, buffer), that the lead belongs to this clinic, and that
    /// the lead has no other upcoming booked/confirmed appointment (LeadAlreadyBookedException). Fills in a missing end time from the
    /// procedure/default duration. Throws SlotUnavailableException when it is not. Used by the AI's book_consultation.</summary>
    Task<AppointmentResponse> BookAvailableSlotAsync(CreateAppointmentRequest request, CancellationToken ct = default);

    Task<AppointmentResponse> CreateAsync(CreateAppointmentRequest request, CancellationToken ct = default);

    Task<AppointmentResponse?> GetByIdAsync(Guid clinicId, Guid id, CancellationToken ct = default);

    Task<AppointmentResponse?> UpdateStatusAsync(Guid clinicId, Guid id, string status, CancellationToken ct = default);

    /// <summary>The AI's reschedule_consultation: moves ONE of this lead's own upcoming (booked/confirmed) appointments to a new time.
    /// Same rules and per-clinic lock as booking (hours, exceptions, notice, horizon, overlaps, buffer), with the appointment
    /// itself excluded from the overlap check. Status returns to Booked. Returns null when the appointment doesn't exist for this
    /// clinic AND lead; ArgumentException when it can't be moved (past, canceled, attended...); SlotUnavailableException when the
    /// new time isn't bookable.</summary>
    Task<AppointmentResponse?> RescheduleAsync(
        Guid clinicId, Guid leadId, Guid id, DateTimeOffset newStart, DateTimeOffset? newEnd, string? reason, CancellationToken ct = default);

    /// <summary>The AI's cancel_consultation: cancels one of this lead's own upcoming appointments and records the reason.
    /// Null when it doesn't exist for this clinic AND lead; ArgumentException when it can't be canceled (already finished etc.).</summary>
    Task<AppointmentResponse?> CancelAsync(Guid clinicId, Guid leadId, Guid id, string? reason, CancellationToken ct = default);

    /// <summary>The AI's get_my_appointments: this lead's future booked/confirmed appointments, soonest first, in clinic-local wording.</summary>
    Task<UpcomingAppointmentsResponse> GetUpcomingForLeadAsync(Guid clinicId, Guid leadId, CancellationToken ct = default);

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

/// <summary>The lead already has an upcoming booked/confirmed appointment; book_consultation refuses to make a second one.
/// Existing describes it so the AI can offer to move it instead.</summary>
public class LeadAlreadyBookedException : Exception
{
    public LeadAlreadyBookedException(Dtos.UpcomingAppointmentResponse existing)
        : base($"This patient already has an upcoming appointment ({existing.Label}). Reschedule or cancel it instead of booking another.")
    {
        Existing = existing;
    }

    public Dtos.UpcomingAppointmentResponse Existing { get; }
}
