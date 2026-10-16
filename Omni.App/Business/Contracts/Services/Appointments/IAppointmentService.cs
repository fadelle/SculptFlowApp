using Omni.App.Entities.Dtos.Appointments;
using Omni.App.Entities.Requests.Ai;
using Omni.App.Entities.Requests.Appointments;
using Omni.App.Entities.Responses.Appointments;

namespace Omni.App.Business.Contracts.Services.Appointments;

public interface IAppointmentService
{
    /// <summary>Books through the same insert as CreateAsync, but first re-checks — under a per-client lock, so two
    /// simultaneous attempts for one slot cannot both succeed — that the slot is still bookable per IAvailabilityService
    /// (weekly schedule, exceptions, notice, horizon, existing appointments, buffer), that the lead belongs to this client, and that
    /// the lead has no other upcoming booked/confirmed appointment (LeadAlreadyBookedException). Fills in a missing end time from the
    /// service/default duration. Throws SlotUnavailableException when it is not. Used by the AI's book_consultation.</summary>
    Task<AppointmentResponse> BookAvailableSlotAsync(CreateAppointmentRequest request, CancellationToken ct = default);

    Task<AppointmentResponse> CreateAsync(CreateAppointmentRequest request, CancellationToken ct = default);

    Task<AppointmentResponse?> GetByIdAsync(Guid clientId, Guid id, CancellationToken ct = default);

    Task<AppointmentResponse?> UpdateStatusAsync(Guid clientId, Guid id, string status, CancellationToken ct = default);

    /// <summary>The AI's reschedule_consultation: moves ONE of this lead's own upcoming (booked/confirmed) appointments to a new time. id is optional: when omitted the lead's single upcoming appointment is used (MultipleUpcomingAppointmentsException if there are several).
    /// Same rules and per-client lock as booking (hours, exceptions, notice, horizon, overlaps, buffer), with the appointment
    /// itself excluded from the overlap check. Status returns to Booked. Returns null when the appointment doesn't exist for this
    /// client AND lead; ArgumentException when it can't be moved (past, canceled, attended...); SlotUnavailableException when the
    /// new time isn't bookable.</summary>
    Task<AppointmentResponse?> RescheduleAsync(
        Guid clientId, Guid leadId, Guid? id, DateTimeOffset newStart, DateTimeOffset? newEnd, string? reason, CancellationToken ct = default);

    /// <summary>The AI's cancel_consultation: cancels one of this lead's own upcoming appointments and records the reason.
    /// Null when it doesn't exist for this client AND lead; ArgumentException when it can't be canceled (already finished etc.).</summary>
    Task<AppointmentResponse?> CancelAsync(Guid clientId, Guid leadId, Guid? id, string? reason, CancellationToken ct = default);

    /// <summary>The AI's get_my_appointments: this lead's future booked/confirmed appointments, soonest first, in client-local wording.</summary>
    Task<UpcomingAppointmentsResponse> GetUpcomingForLeadAsync(Guid clientId, Guid leadId, CancellationToken ct = default);

    /// <summary>What get_available_slots reports alongside the slots: the lead's upcoming appointments and the backend's explicit
    /// CanCreateNewBooking decision. Uses the SAME definition of "upcoming" as get_my_appointments and the book_consultation guard
    /// (LoadUpcomingAsync). Returns null when the lead does not belong to this client (never leaks another client's data).</summary>
    Task<PatientBookingContext?> GetBookingContextAsync(Guid clientId, Guid leadId, CancellationToken ct = default);

    /// <summary>The AI's single scheduling mutation (schedule_consultation). The BACKEND decides from the real appointment state:
    /// no upcoming appointment → create one (BookAvailableSlotAsync); one upcoming appointment AND the patient clearly agreed
    /// (request.ConfirmReplaceExisting) → move it (RescheduleAsync); an upcoming appointment without that consent → change nothing
    /// and return CONFIRMATION_REQUIRED. Every path reuses the existing validations (availability recheck under the per-client lock,
    /// one-upcoming-per-lead guard, lead/client ownership, status rules). Failures come back as outcomes, not exceptions.</summary>
    Task<ScheduleOutcome> ScheduleAsync(Guid clientId, Guid leadId, ScheduleConsultationRequest request, CancellationToken ct = default);

    /// <summary>The AI's cancel_consultation with an unambiguous outcome: wraps CancelAsync (same ownership and status rules) and reports the
    /// canceled appointment in client-local time, or why nothing was canceled. Failures come back as outcomes, not exceptions.</summary>
    Task<CancelOutcome> CancelConsultationAsync(Guid clientId, Guid leadId, Guid? id, string? reason, CancellationToken ct = default);

    Task<(IReadOnlyList<AppointmentResponse> Items, int TotalCount)> ListAsync(
        Guid clientId, string? status, DateTimeOffset? from, DateTimeOffset? to, int skip, int take, CancellationToken ct = default);

    /// <summary>Every appointment in the visible month calendar grid for the Appointments page — the grid's
    /// leading/trailing days from adjacent months included, everything grouped by LOCAL calendar day in
    /// <paramref name="displayTimeZone"/> — the viewer's, when they switched to "My time" — else the client's own. No
    /// status filter: booked, confirmed, canceled and completed appointments all appear, same as the rest of the app.</summary>
    Task<CalendarMonthResponse> GetCalendarMonthAsync(Guid clientId, int year, int month, TimeZoneInfo? displayTimeZone = null, CancellationToken ct = default);
}
