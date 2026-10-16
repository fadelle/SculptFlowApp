using Omni.App.Entities.Dtos.Bookings;
using Omni.App.Entities.Requests.Ai;
using Omni.App.Entities.Requests.Bookings;
using Omni.App.Entities.Responses.Bookings;

namespace Omni.App.Business.Contracts.Services.Bookings;

public interface IBookingService
{
    /// <summary>Books through the same insert as CreateAsync, but first re-checks — under a per-client lock, so two
    /// simultaneous attempts for one slot cannot both succeed — that the slot is still bookable per IAvailabilityService
    /// (weekly schedule, exceptions, notice, horizon, existing bookings, buffer), that the contact belongs to this client, and that
    /// the contact has no other upcoming booked/confirmed booking (ContactAlreadyBookedException). Fills in a missing end time from the
    /// service/default duration. Throws SlotUnavailableException when it is not. Used by the AI's book_slot.</summary>
    Task<BookingResponse> BookAvailableSlotAsync(CreateBookingRequest request, CancellationToken ct = default);

    Task<BookingResponse> CreateAsync(CreateBookingRequest request, CancellationToken ct = default);

    Task<BookingResponse?> GetByIdAsync(Guid clientId, Guid id, CancellationToken ct = default);

    Task<BookingResponse?> UpdateStatusAsync(Guid clientId, Guid id, string status, CancellationToken ct = default);

    /// <summary>The AI's reschedule_booking: moves ONE of this contact's own upcoming (booked/confirmed) bookings to a new time. id is optional: when omitted the contact's single upcoming booking is used (MultipleUpcomingBookingsException if there are several).
    /// Same rules and per-client lock as booking (hours, exceptions, notice, horizon, overlaps, buffer), with the booking
    /// itself excluded from the overlap check. Status returns to Booked. Returns null when the booking doesn't exist for this
    /// client AND contact; ArgumentException when it can't be moved (past, canceled, attended...); SlotUnavailableException when the
    /// new time isn't bookable.</summary>
    Task<BookingResponse?> RescheduleAsync(
        Guid clientId, Guid contactId, Guid? id, DateTimeOffset newStart, DateTimeOffset? newEnd, string? reason, CancellationToken ct = default);

    /// <summary>The AI's cancel_booking: cancels one of this contact's own upcoming bookings and records the reason.
    /// Null when it doesn't exist for this client AND contact; ArgumentException when it can't be canceled (already finished etc.).</summary>
    Task<BookingResponse?> CancelAsync(Guid clientId, Guid contactId, Guid? id, string? reason, CancellationToken ct = default);

    /// <summary>The AI's get_my_bookings: this contact's future booked/confirmed bookings, soonest first, in client-local wording.</summary>
    Task<UpcomingBookingsResponse> GetUpcomingForContactAsync(Guid clientId, Guid contactId, CancellationToken ct = default);

    /// <summary>What get_available_slots reports alongside the slots: the contact's upcoming bookings and the backend's explicit
    /// CanCreateNewBooking decision. Uses the SAME definition of "upcoming" as get_my_bookings and the book_slot guard
    /// (LoadUpcomingAsync). Returns null when the contact does not belong to this client (never leaks another client's data).</summary>
    Task<ContactBookingContext?> GetBookingContextAsync(Guid clientId, Guid contactId, CancellationToken ct = default);

    /// <summary>The AI's single scheduling mutation (schedule_booking). The BACKEND decides from the real booking state:
    /// no upcoming booking → create one (BookAvailableSlotAsync); one upcoming booking AND the contact clearly agreed
    /// (request.ConfirmReplaceExisting) → move it (RescheduleAsync); an upcoming booking without that consent → change nothing
    /// and return CONFIRMATION_REQUIRED. Every path reuses the existing validations (availability recheck under the per-client lock,
    /// one-upcoming-per-contact guard, contact/client ownership, status rules). Failures come back as outcomes, not exceptions.</summary>
    Task<ScheduleOutcome> ScheduleAsync(Guid clientId, Guid contactId, ScheduleBookingRequest request, CancellationToken ct = default);

    /// <summary>The AI's cancel_booking with an unambiguous outcome: wraps CancelAsync (same ownership and status rules) and reports the
    /// canceled booking in client-local time, or why nothing was canceled. Failures come back as outcomes, not exceptions.</summary>
    Task<CancelOutcome> CancelBookingAsync(Guid clientId, Guid contactId, Guid? id, string? reason, CancellationToken ct = default);

    Task<(IReadOnlyList<BookingResponse> Items, int TotalCount)> ListAsync(
        Guid clientId, string? status, DateTimeOffset? from, DateTimeOffset? to, int skip, int take, CancellationToken ct = default);

    /// <summary>Every booking in the visible month calendar grid for the Bookings page — the grid's
    /// leading/trailing days from adjacent months included, everything grouped by LOCAL calendar day in
    /// <paramref name="displayTimeZone"/> — the viewer's, when they switched to "My time" — else the client's own. No
    /// status filter: booked, confirmed, canceled and completed bookings all appear, same as the rest of the app.</summary>
    Task<CalendarMonthResponse> GetCalendarMonthAsync(Guid clientId, int year, int month, TimeZoneInfo? displayTimeZone = null, CancellationToken ct = default);
}
