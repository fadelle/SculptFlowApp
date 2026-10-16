using System.Text.Json;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Bookings;
using Omni.App.Business.Contracts.Services.BusinessTypes;
using Omni.App.Business.Contracts.Services.Calendars;
using Omni.App.Business.Contracts.Services.Notifications;
using Omni.App.Business.Contracts.Services.Catalog;
using Omni.App.Business.Mappers.Bookings;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Bookings;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Ai;
using Omni.App.Entities.Requests.Bookings;
using Omni.App.Entities.Responses.Bookings;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Bookings;
using Omni.App.Persistence.Contracts.Campaigns;
using Omni.App.Persistence.Contracts.Clients;
using Omni.App.Persistence.Contracts.Contacts;
using Omni.App.Persistence.Contracts.Catalog;

namespace Omni.App.Business.Services.Bookings;

public class BookingService : IBookingService
{
    private readonly IBookingRepository _bookings;
    private readonly IContactRepository _contacts;
    private readonly IClientRepository _clients;
    private readonly IServiceRepository _serviceRows;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEventLogger _events;
    private readonly ICatalogService _services;

    private readonly IAvailabilityService _availability;
    private readonly IInboxNotifier _notifier;
    private readonly INotificationService _notifications;
    private readonly ICalendarIntegrationService _calendarSync;
    private readonly ICampaignRepository _campaigns;
    private readonly IConfigManager _config;
    private readonly IBusinessTypeService _businessTypes;

    public BookingService(
        IBookingRepository bookings, IContactRepository contacts, IClientRepository clients, IServiceRepository serviceRows, IUnitOfWork unitOfWork, IEventLogger events, ICatalogService services, IAvailabilityService availability,
        IInboxNotifier notifier, INotificationService notifications, ICalendarIntegrationService calendarSync,
        ICampaignRepository campaigns, IConfigManager config, IBusinessTypeService businessTypes)
    {
        _campaigns = campaigns;
        _config = config;
        _businessTypes = businessTypes;
        _bookings = bookings;
        _contacts = contacts;
        _clients = clients;
        _serviceRows = serviceRows;
        _unitOfWork = unitOfWork;
        _events = events;
        _services = services;
        _availability = availability;
        _notifier = notifier;
        _notifications = notifications;
        _calendarSync = calendarSync;
    }

    /// <summary>The client's configured timezone (Client.Timezone), falling back to UTC for an unrecognized
    /// value — the one place "what timezone is this client in" is resolved, shared by slot generation and the
    /// calendar's day grouping so both agree on what day/time a booking falls on.</summary>
    private static TimeZoneInfo ResolveTimeZone(Client client)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(client.Timezone);
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }

    public async Task<BookingResponse> BookAvailableSlotAsync(CreateBookingRequest request, CancellationToken ct = default)
    {
        await using var tx = await _unitOfWork.BeginTransactionAsync(ct);

        // Serialize bookings per client until commit: the second of two concurrent attempts waits here, then its check
        // below sees the first one's booking and rejects it.
        await LockClientAsync(request.ClientId, ct);

        // The contact must be one of THIS client's (a wrong or foreign id must not create a booking).
        if (!await _contacts.ExistsAsync(request.ClientId, request.ContactId, ct))
        {
            throw new ArgumentException("Contact not found for this client.");
        }

        // One upcoming booking per contact: a second booking is refused, and the reply describes the existing one so the AI can
        // offer to move it. Under the lock, so two simultaneous bookings for the same contact can't both pass.
        var (tz, upcoming) = await LoadUpcomingAsync(request.ClientId, request.ContactId, ct);
        if (upcoming.Count > 0) throw new ContactAlreadyBookedException(ToUpcoming(upcoming[0], tz));

        var check = await _availability.CheckSlotAsync(request.ClientId, request.ServiceId, request.ScheduledStart, request.ScheduledEnd, ct: ct);
        if (check.Error is not null) throw new SlotUnavailableException(check.Error);

        var booking = await CreateCoreAsync(request with { ScheduledEnd = check.End }, ct);
        await tx.CommitAsync(ct);
        await NotifyChangedAsync(request.ClientId, booking, "created", ct); // only once the row is committed and visible
        return booking;
    }

    /// <summary>Tells the client's open calendars to re-fetch (best-effort SignalR) AND — for created/rescheduled/canceled — creates
    /// the matching BOOKING_* notification (see INotificationService; "status_changed" covers attended/no_show/confirmed, which
    /// aren't in the notification list). Called only once the booking row is actually committed — never on a mere request.</summary>
    private async Task NotifyChangedAsync(Guid clientId, BookingResponse booking, string change, CancellationToken ct)
    {
        try { await _notifier.BookingChangedAsync(clientId, booking.Id, change, ct); }
        catch { /* best effort */ }

        // Only AFTER the booking change is already committed — TriggerBookingSyncAsync itself decides
        // whether "change" maps to a real external-calendar operation (created/rescheduled/canceled only) and
        // never throws, so a calendar problem can never affect the booking operation that already succeeded.
        await _calendarSync.TriggerBookingSyncAsync(clientId, booking, change, ct);

        var (type, title) = change switch
        {
            "created" => (NotificationType.BookingCreated, "Booking made"),
            "rescheduled" => (NotificationType.BookingRescheduled, "Booking rescheduled"),
            "canceled" => (NotificationType.BookingCanceled, "Booking canceled"),
            _ => (null, null)
        };
        if (type is null) return;

        // A [[time:…]] marker, not text: notifications.js shows it in each viewer's own timezone (OmniTime.expand).
        var whenLabel = $"[[time:{booking.ScheduledStart.ToUniversalTime():yyyy-MM-dd'T'HH:mm:ss'Z'}]]";
        var who = string.IsNullOrWhiteSpace(booking.ContactFullName) ? "A contact" : booking.ContactFullName!;
        var what = string.IsNullOrWhiteSpace(booking.ServiceName) ? "a slot" : booking.ServiceName!;
        var verb = change switch { "created" => "booked", "rescheduled" => "moved to", _ => "canceled for" };

        await _notifications.CreateAsync(clientId, type, title!,
            $"{who} — {what} {verb} {whenLabel}.",
            contactId: booking.ContactId, bookingId: booking.Id,
            link: $"/dashboard/bookings/{booking.Id}", ct: ct);
    }

    public async Task<BookingResponse> CreateAsync(CreateBookingRequest request, CancellationToken ct = default)
    {
        if (!await _contacts.ExistsAsync(request.ClientId, request.ContactId, ct))
        {
            throw new ArgumentException("Contact not found for this client.");
        }
        var booking = await CreateCoreAsync(request, ct);
        await NotifyChangedAsync(request.ClientId, booking, "created", ct);
        return booking;
    }

    private async Task<BookingResponse> CreateCoreAsync(CreateBookingRequest request, CancellationToken ct)
    {
        // A new booking may only reference one of this client's ACTIVE services.
        if (request.ServiceId is { } serviceId)
        {
            await _services.EnsureUsableAsync(request.ClientId, serviceId, ct);
        }

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            ClientId = request.ClientId,
            ContactId = request.ContactId,
            ServiceId = request.ServiceId,
            BookingType = string.IsNullOrWhiteSpace(request.BookingType) ? "standard" : request.BookingType,
            Status = BookingStatus.Booked,
            // Npgsql only stores UTC-offset values; callers (n8n) send the client-local offset returned by get_available_slots.
            ScheduledStart = request.ScheduledStart.ToUniversalTime(),
            ScheduledEnd = request.ScheduledEnd?.ToUniversalTime(),
            LocationType = request.LocationType,
            LocationName = request.LocationName,
            Notes = request.Notes,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _bookings.Add(booking);

        var contact = await _contacts.GetAsync(request.ClientId, request.ContactId, ct);
        if (contact is not null)
        {
            var pipeline = await _businessTypes.GetPipelineAsync(request.ClientId, ct);
            contact.StageKey = pipeline.MoveTo(contact.StageKey, StageCategory.Booked) ?? contact.StageKey;
            contact.UpdatedAt = DateTimeOffset.UtcNow;
        }

        _events.Log(request.ClientId, EventTypes.BookingCreated, contactId: request.ContactId, bookingId: booking.Id);

        // Credit the campaign the contact replied to: the booking shows up in that campaign's "Booked" figure.
        var now = DateTimeOffset.UtcNow;
        var campaignSend = await _campaigns.FindLatestRepliedAwaitingBookingAsync(
            request.ClientId, request.ContactId, now.AddDays(-_config.CampaignsBookingAttributionDays), ct);
        if (campaignSend is not null)
        {
            campaignSend.Status = CampaignRecipientStatus.Booked;
            campaignSend.BookedAt = now;
            campaignSend.UpdatedAt = now;
        }

        await _unitOfWork.SaveChangesAsync(ct);

        return await ToResponseAsync(booking, ct);
    }

    public async Task<BookingResponse?> GetByIdAsync(Guid clientId, Guid id, CancellationToken ct = default)
    {
        var booking = await _bookings.GetWithDetailsAsync(clientId, id, ct);
        return booking is null ? null : BookingMapper.ToResponse(booking);
    }

    public async Task<BookingResponse?> UpdateStatusAsync(Guid clientId, Guid id, string status, CancellationToken ct = default)
    {
        if (!BookingStatus.All.Contains(status))
        {
            throw new ArgumentException($"Invalid booking status '{status}'.", nameof(status));
        }

        var booking = await _bookings.GetAsync(clientId, id, ct);
        if (booking is null) return null;

        booking.Status = status;
        booking.UpdatedAt = DateTimeOffset.UtcNow;

        var contact = await _contacts.GetByIdAsync(booking.ContactId, ct);
        if (contact is not null)
        {
            var pipeline = await _businessTypes.GetPipelineAsync(clientId, ct);
            if (status == BookingStatus.Attended)
            {
                contact.StageKey = pipeline.MoveTo(contact.StageKey, StageCategory.Attended) ?? contact.StageKey;
                _events.Log(clientId, EventTypes.BookingAttended, contactId: contact.Id, bookingId: booking.Id);
            }
            else if (status == BookingStatus.NoShow)
            {
                contact.StageKey = pipeline.MoveTo(contact.StageKey, StageCategory.NoShow) ?? contact.StageKey;
                _events.Log(clientId, EventTypes.BookingNoShow, contactId: contact.Id, bookingId: booking.Id);
            }
            contact.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _unitOfWork.SaveChangesAsync(ct);
        var updateResponse = await ToResponseAsync(booking, ct);
        await NotifyChangedAsync(clientId, updateResponse, status == BookingStatus.Canceled ? "canceled" : "status_changed", ct);
        return updateResponse;
    }

    public async Task<BookingResponse?> RescheduleAsync(
        Guid clientId, Guid contactId, Guid? id, DateTimeOffset newStart, DateTimeOffset? newEnd, string? reason, CancellationToken ct = default)
    {
        await using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        await LockClientAsync(clientId, ct);

        // The booking must belong to this client AND this contact: the AI only ever acts on the contact it is talking to.
        var targetId = await ResolveTargetAsync(clientId, contactId, id, ct);
        if (targetId is null) return null;
        var booking = await _bookings.GetForContactAsync(clientId, contactId, targetId.Value, ct);
        if (booking is null) return null;

        if (booking.Status != BookingStatus.Booked && booking.Status != BookingStatus.Confirmed)
        {
            throw new ArgumentException($"This booking is {booking.Status} and can't be rescheduled.");
        }
        if (booking.ScheduledStart <= DateTimeOffset.UtcNow)
        {
            throw new ArgumentException("This booking is already in the past and can't be rescheduled. Book a new one instead.");
        }

        // A service that was deactivated since booking can't be newly booked, but must not trap the contact in an old time:
        // fall back to the client's default duration for it.
        var serviceId = await ActiveServiceOrNullAsync(booking.ServiceId, ct);

        var check = await _availability.CheckSlotAsync(clientId, serviceId, newStart, newEnd, excludeBookingId: booking.Id, ct: ct);
        if (check.Error is not null) throw new SlotUnavailableException(check.Error);

        booking.ScheduledStart = newStart.ToUniversalTime();
        booking.ScheduledEnd = check.End.ToUniversalTime();
        // A rescheduled time isn't "confirmed" again until the client/staff confirms it — same
        // status a brand-new booking starts at.
        booking.Status = BookingStatus.Booked;
        booking.UpdatedAt = DateTimeOffset.UtcNow;

        _events.Log(clientId, EventTypes.BookingRescheduled, contactId: booking.ContactId, bookingId: booking.Id,
            metadataJson: JsonSerializer.Serialize(new { reason }));

        await _unitOfWork.SaveChangesAsync(ct);
        var response = await ToResponseAsync(booking, ct);
        await tx.CommitAsync(ct);
        await NotifyChangedAsync(clientId, response, "rescheduled", ct); // after the transaction commits
        return response;
    }

    public async Task<BookingResponse?> CancelAsync(Guid clientId, Guid contactId, Guid? id, string? reason, CancellationToken ct = default)
    {
        var targetId = await ResolveTargetAsync(clientId, contactId, id, ct);
        if (targetId is null) return null;
        var booking = await _bookings.GetForContactAsync(clientId, contactId, targetId.Value, ct);
        if (booking is null) return null;

        if (booking.Status == BookingStatus.Canceled) return await ToResponseAsync(booking, ct); // already done

        if (booking.Status != BookingStatus.Booked && booking.Status != BookingStatus.Confirmed)
        {
            throw new ArgumentException($"This booking is {booking.Status} and can't be canceled.");
        }

        booking.Status = BookingStatus.Canceled;
        booking.UpdatedAt = DateTimeOffset.UtcNow;

        _events.Log(clientId, EventTypes.BookingCanceled, contactId: booking.ContactId, bookingId: booking.Id,
            metadataJson: JsonSerializer.Serialize(new { reason }));

        await _unitOfWork.SaveChangesAsync(ct);
        var cancelResponse = await ToResponseAsync(booking, ct);
        await NotifyChangedAsync(clientId, cancelResponse, "canceled", ct);
        return cancelResponse;
    }

    public async Task<ContactBookingContext?> GetBookingContextAsync(Guid clientId, Guid contactId, CancellationToken ct = default)
    {
        // A contactId from another client (or a made-up one) must look exactly like "not found" — never return its bookings.
        if (!await _contacts.ExistsAsync(clientId, contactId, ct)) return null;

        var (tz, upcoming) = await LoadUpcomingAsync(clientId, contactId, ct);
        var existing = upcoming.Select(a => ToUpcoming(a, tz)).ToList();
        var blocked = existing.Count > 0;
        return new ContactBookingContext(blocked, !blocked, blocked ? "existing_upcoming_booking" : null, existing);
    }

    public async Task<ScheduleOutcome> ScheduleAsync(Guid clientId, Guid contactId, ScheduleBookingRequest request, CancellationToken ct = default)
    {
        // The current state always comes from the database (never from what the AI remembers). Null = the contact isn't in this client.
        var context = await GetBookingContextAsync(clientId, contactId, ct);
        if (context is null) return new ScheduleOutcome("none", "CONTACT_NOT_FOUND", "Contact not found for this client.");

        var tz = await GetTimeZoneAsync(clientId, ct);
        var requestedLabel = LabelFor(TimeZoneInfo.ConvertTime(request.ScheduledStart, tz));
        var existing = context.ExistingUpcomingBookings;

        try
        {
            // 1) No upcoming booking → create. BookAvailableSlotAsync still re-checks the slot and the duplicate guard under the lock,
            //    so a booking that landed a moment ago surfaces as ContactAlreadyBookedException below.
            if (!context.HasUpcomingBooking)
            {
                var created = await BookAvailableSlotAsync(new CreateBookingRequest(
                    clientId, contactId, request.ServiceId,
                    string.IsNullOrWhiteSpace(request.BookingType) ? "standard" : request.BookingType,
                    request.ScheduledStart, null, null, null, request.Notes), ct);
                return new ScheduleOutcome("created", "BOOKED", "Booking made.",
                    Booking: await ToLocalAsync(clientId, contactId, created.Id, ct), RequestedLabel: requestedLabel);
            }

            // 2) The contact already has an upcoming booking. Which one would be replaced?
            // Exactly ONE upcoming booking: the backend selects it itself and bookingId is ignored completely (the AI's ids go
            // stale after any booking/cancel). Two or more: a bookingId is required and must be one of the CURRENT upcoming ones,
            // otherwise nothing is mutated.
            UpcomingBookingResponse? target = null;
            if (existing.Count == 1)
            {
                target = existing[0];
            }
            else if (request.BookingId is { } wanted)
            {
                target = existing.FirstOrDefault(e => e.Id == wanted);
                if (target is null)
                {
                    return new ScheduleOutcome("none", "INVALID_REQUEST",
                        "That booking is not one of this contact's current upcoming bookings.", Existing: existing, RequestedLabel: requestedLabel);
                }
            }

            // Read-only pre-check, so the contact is never asked to confirm a move to a time that cannot work.
            var check = await _availability.CheckSlotAsync(clientId, await ActiveServiceOrNullAsync(target?.ServiceId ?? request.ServiceId, ct),
                request.ScheduledStart, null, excludeBookingId: target?.Id, ct: ct);
            if (check.Error is not null)
            {
                return new ScheduleOutcome("none", "SLOT_UNAVAILABLE", check.Error, Existing: existing, RequestedLabel: requestedLabel);
            }

            // 3) No explicit consent → change NOTHING. Asking about another date is not permission to move the current booking.
            if (!request.ConfirmReplaceExisting)
            {
                return new ScheduleOutcome("none", "CONFIRMATION_REQUIRED",
                    "Contact already has an upcoming booking; nothing was changed.", Existing: existing, RequestedLabel: requestedLabel);
            }

            // 4) Consent given. With several upcoming bookings the contact must say which one.
            if (target is null)
            {
                return new ScheduleOutcome("none", "MULTIPLE_UPCOMING_BOOKINGS",
                    "Contact has more than one upcoming booking; it is unclear which to move.", Existing: existing, RequestedLabel: requestedLabel);
            }

            var moved = await RescheduleAsync(clientId, contactId, target.Id, request.ScheduledStart, null, request.Reason, ct);
            if (moved is null)
            {
                return new ScheduleOutcome("none", "INVALID_REQUEST", "The booking could not be found for this contact.", RequestedLabel: requestedLabel);
            }
            return new ScheduleOutcome("rescheduled", "RESCHEDULED", "Booking rescheduled.",
                Booking: await ToLocalAsync(clientId, contactId, moved.Id, ct), PreviousBooking: target, RequestedLabel: requestedLabel);
        }
        catch (ContactAlreadyBookedException ex)
        {
            // Another booking landed between the state read and the locked check: same answer as "already has one".
            return new ScheduleOutcome("none", "CONFIRMATION_REQUIRED", "Contact already has an upcoming booking; nothing was changed.",
                Existing: new[] { ex.Existing }, RequestedLabel: requestedLabel);
        }
        catch (SlotUnavailableException ex)
        {
            return new ScheduleOutcome("none", "SLOT_UNAVAILABLE", ex.Message, Existing: existing.Count > 0 ? existing : null, RequestedLabel: requestedLabel);
        }
        catch (ArgumentException ex)
        {
            return new ScheduleOutcome("none", "INVALID_REQUEST", ex.Message, RequestedLabel: requestedLabel);
        }
    }

    private async Task<TimeZoneInfo> GetTimeZoneAsync(Guid clientId, CancellationToken ct)
    {
        var client = await _clients.GetReadOnlyAsync(clientId, ct);
        return client is null ? TimeZoneInfo.Utc : ResolveTimeZone(client);
    }

    /// <summary>A service id only when that service is still active — a deactivated one falls back to the client's default duration.</summary>
    private async Task<Guid?> ActiveServiceOrNullAsync(Guid? serviceId, CancellationToken ct)
    {
        if (serviceId is not { } id) return null;
        return await _serviceRows.IsActiveAsync(id, ct) ? id : null;
    }

    private async Task<UpcomingBookingResponse?> ToLocalAsync(Guid clientId, Guid contactId, Guid bookingId, CancellationToken ct)
    {
        var (tz, items) = await LoadUpcomingAsync(clientId, contactId, ct);
        var a = items.FirstOrDefault(x => x.Id == bookingId);
        return a is null ? null : ToUpcoming(a, tz);
    }

    private static string LabelFor(DateTimeOffset local)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return $"{local.DayOfWeek}, {local.ToString("MMM d", inv)} at {local.ToString("h:mm tt", inv)}";
    }

    public async Task<CancelOutcome> CancelBookingAsync(Guid clientId, Guid contactId, Guid? id, string? reason, CancellationToken ct = default)
    {
        try
        {
            // Same rule as schedule_booking: no upcoming → nothing to cancel; exactly ONE → the backend selects it and any bookingId
            // is ignored (the AI's ids go stale); two or more → the id is required and must be a CURRENT upcoming booking.
            var context = await GetBookingContextAsync(clientId, contactId, ct);
            var existing = context?.ExistingUpcomingBookings;
            if (existing is null || existing.Count == 0)
            {
                return new CancelOutcome("none", "NO_UPCOMING_BOOKING", "This contact has no upcoming booking to cancel (it may already be canceled).");
            }
            if (existing.Count > 1 && id is { } wanted && !existing.Any(e => e.Id == wanted))
            {
                return new CancelOutcome("none", "INVALID_REQUEST",
                    "That booking is not one of this contact's current upcoming bookings.", Existing: existing);
            }

            var canceled = await CancelAsync(clientId, contactId, existing.Count == 1 ? null : id, reason, ct);
            if (canceled is null)
            {
                return new CancelOutcome("none", "NO_UPCOMING_BOOKING", "This contact has no upcoming booking to cancel (it may already be canceled).");
            }

            // CancelAsync returns the stored (UTC) booking; report it the way the contact should hear it — client-local.
            var tz = await GetTimeZoneAsync(clientId, ct);
            var entity = await _bookings.GetWithServiceReadOnlyAsync(canceled.Id, ct)
                ?? throw new InvalidOperationException("Canceled booking not found.");
            return new CancelOutcome("canceled", "CANCELED", "Booking canceled.", Booking: ToUpcoming(entity, tz));
        }
        catch (MultipleUpcomingBookingsException ex)
        {
            return new CancelOutcome("none", "MULTIPLE_UPCOMING_BOOKINGS", ex.Message, Existing: ex.Bookings);
        }
        catch (ArgumentException ex)
        {
            return new CancelOutcome("none", "INVALID_REQUEST", ex.Message);
        }
    }

    public async Task<UpcomingBookingsResponse> GetUpcomingForContactAsync(Guid clientId, Guid contactId, CancellationToken ct = default)
    {
        var (tz, items) = await LoadUpcomingAsync(clientId, contactId, ct);
        return new UpcomingBookingsResponse(tz.Id, items.Select(a => ToUpcoming(a, tz)).ToList());
    }

    /// <summary>The booking a reschedule/cancel acts on: the given id, or — when the AI sends none — the contact's single upcoming
    /// booking. Null when the contact has none; several upcoming ones (only staff can create them) need an explicit id.</summary>
    private async Task<Guid?> ResolveTargetAsync(Guid clientId, Guid contactId, Guid? id, CancellationToken ct)
    {
        if (id is not null) return id;

        var (tz, upcoming) = await LoadUpcomingAsync(clientId, contactId, ct);
        if (upcoming.Count == 0) return null;
        if (upcoming.Count > 1) throw new MultipleUpcomingBookingsException(upcoming.Select(a => ToUpcoming(a, tz)).ToList());
        return upcoming[0].Id;
    }

    /// <summary>The contact's future booked/confirmed bookings, soonest first (canceled, rescheduled, attended and no-show never
    /// count as "upcoming"), plus the client's timezone for wording them.</summary>
    private async Task<(TimeZoneInfo Tz, List<Booking> Items)> LoadUpcomingAsync(Guid clientId, Guid contactId, CancellationToken ct)
    {
        var client = await _clients.GetReadOnlyAsync(clientId, ct);
        var tz = client is null ? TimeZoneInfo.Utc : ResolveTimeZone(client);
        var now = DateTimeOffset.UtcNow;

        var items = (await _bookings.ListUpcomingForContactAsync(clientId, contactId, now, ct)).ToList();
        return (tz, items);
    }

    private static UpcomingBookingResponse ToUpcoming(Booking a, TimeZoneInfo tz)
    {
        var start = TimeZoneInfo.ConvertTime(a.ScheduledStart, tz);
        DateTimeOffset? end = a.ScheduledEnd is null ? null : TimeZoneInfo.ConvertTime(a.ScheduledEnd.Value, tz);
        return new UpcomingBookingResponse(
            a.Id, a.Status, a.BookingType, a.ServiceId, a.Service?.Name, start, end,
            start.ToString("yyyy-MM-dd"), start.ToString("HH:mm"),
            LabelFor(start));
    }

    /// <summary>Serializes bookings per client until the surrounding transaction ends.</summary>
    private Task LockClientAsync(Guid clientId, CancellationToken ct) => _bookings.LockClientForBookingAsync(clientId, ct);

    public async Task<(IReadOnlyList<BookingResponse> Items, int TotalCount)> ListAsync(
        Guid clientId, string? status, DateTimeOffset? from, DateTimeOffset? to, int skip, int take, CancellationToken ct = default)
    {
        var (items, totalCount) = await _bookings.ListAsync(clientId, status, from, to, skip, take, ct);

        return (items.Select(BookingMapper.ToResponse).ToList(), totalCount);
    }

    public async Task<CalendarMonthResponse> GetCalendarMonthAsync(Guid clientId, int year, int month, TimeZoneInfo? displayTimeZone = null, CancellationToken ct = default)
    {
        var client = await _clients.GetReadOnlyAsync(clientId, ct);
        var clientTz = client is null ? TimeZoneInfo.Utc : ResolveTimeZone(client);
        var tz = displayTimeZone ?? clientTz;

        var firstOfMonth = new DateOnly(year, month, 1);
        // Sunday-start weeks (no existing calendar convention in this project to match yet). Pad the grid with
        // whole leading/trailing weeks so every row is a complete Sun–Sat week, same as any standard month view.
        var gridStart = firstOfMonth.AddDays(-(int)firstOfMonth.DayOfWeek);
        var lastOfMonth = firstOfMonth.AddMonths(1).AddDays(-1);
        var gridEnd = lastOfMonth.AddDays(6 - (int)lastOfMonth.DayOfWeek);

        // The grid's local boundaries, converted to UTC — this is the ONLY date range queried, so a month never
        // pulls in the whole bookings table (see IBookingService's remarks).
        var startUtc = LocalMidnightToUtc(gridStart, tz);
        var endUtc = LocalMidnightToUtc(gridEnd.AddDays(1), tz); // exclusive upper bound

        var bookings = await _bookings.ListStartingBetweenAsync(clientId, startUtc, endUtc, ct);

        var items = bookings.Select(a =>
        {
            var local = TimeZoneInfo.ConvertTime(a.ScheduledStart, tz);
            return new CalendarBookingResponse(
                a.Id, a.ContactId, a.Contact?.FullName, a.ServiceId, a.Service?.Name, a.Status,
                a.ScheduledStart, a.ScheduledEnd,
                local.ToString("yyyy-MM-dd"), local.ToString("HH:mm"));
        }).ToList();

        var needsOutcome = await _bookings.CountNeedingOutcomeAsync(clientId, DateTimeOffset.UtcNow, ct);

        return new CalendarMonthResponse(year, month, tz.Id, clientTz.Id, gridStart.ToString("yyyy-MM-dd"), gridEnd.ToString("yyyy-MM-dd"), items, needsOutcome);
    }

    /// <summary>Local midnight on <paramref name="date"/>, in <paramref name="tz"/>, as UTC.</summary>
    private static DateTimeOffset LocalMidnightToUtc(DateOnly date, TimeZoneInfo tz)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, tz.GetUtcOffset(local)).ToUniversalTime();
    }

    private async Task<BookingResponse> ToResponseAsync(Booking a, CancellationToken ct)
    {
        await _bookings.LoadDetailsAsync(a, ct);
        return BookingMapper.ToResponse(a);
    }
}
