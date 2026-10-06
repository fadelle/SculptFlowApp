using System.Text.Json;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Contracts.Services.Appointments;
using PlasticSurgery.Business.Contracts.Services.Calendars;
using PlasticSurgery.Business.Contracts.Services.Notifications;
using PlasticSurgery.Business.Contracts.Services.Procedures;
using PlasticSurgery.Business.Mappers.Appointments;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Exceptions;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Entities.Dtos.Appointments;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Requests.Ai;
using PlasticSurgery.Entities.Requests.Appointments;
using PlasticSurgery.Entities.Responses.Appointments;
using PlasticSurgery.Persistence.Contracts;
using PlasticSurgery.Persistence.Contracts.Appointments;
using PlasticSurgery.Persistence.Contracts.Clinics;
using PlasticSurgery.Persistence.Contracts.Leads;
using PlasticSurgery.Persistence.Contracts.Procedures;

namespace PlasticSurgery.Business.Services.Appointments;

public class AppointmentService : IAppointmentService
{
    private readonly IAppointmentRepository _appointments;
    private readonly ILeadRepository _leads;
    private readonly IClinicRepository _clinics;
    private readonly IProcedureRepository _procedureRows;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEventLogger _events;
    private readonly IProcedureService _procedures;

    private readonly IAvailabilityService _availability;
    private readonly IInboxNotifier _notifier;
    private readonly INotificationService _notifications;
    private readonly ICalendarIntegrationService _calendarSync;

    public AppointmentService(
        IAppointmentRepository appointments, ILeadRepository leads, IClinicRepository clinics, IProcedureRepository procedureRows, IUnitOfWork unitOfWork, IEventLogger events, IProcedureService procedures, IAvailabilityService availability,
        IInboxNotifier notifier, INotificationService notifications, ICalendarIntegrationService calendarSync)
    {
        _appointments = appointments;
        _leads = leads;
        _clinics = clinics;
        _procedureRows = procedureRows;
        _unitOfWork = unitOfWork;
        _events = events;
        _procedures = procedures;
        _availability = availability;
        _notifier = notifier;
        _notifications = notifications;
        _calendarSync = calendarSync;
    }

    /// <summary>The clinic's configured timezone (Clinic.Timezone), falling back to UTC for an unrecognized
    /// value — the one place "what timezone is this clinic in" is resolved, shared by slot generation and the
    /// calendar's day grouping so both agree on what day/time an appointment falls on.</summary>
    private static TimeZoneInfo ResolveTimeZone(Clinic clinic)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(clinic.Timezone);
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }

    public async Task<AppointmentResponse> BookAvailableSlotAsync(CreateAppointmentRequest request, CancellationToken ct = default)
    {
        await using var tx = await _unitOfWork.BeginTransactionAsync(ct);

        // Serialize bookings per clinic until commit: the second of two concurrent attempts waits here, then its check
        // below sees the first one's appointment and rejects it.
        await LockClinicAsync(request.ClinicId, ct);

        // The lead must be one of THIS clinic's (a wrong or foreign id must not create an appointment).
        if (!await _leads.ExistsAsync(request.ClinicId, request.LeadId, ct))
        {
            throw new ArgumentException("Lead not found for this clinic.");
        }

        // One upcoming appointment per lead: a second booking is refused, and the reply describes the existing one so the AI can
        // offer to move it. Under the lock, so two simultaneous bookings for the same lead can't both pass.
        var (tz, upcoming) = await LoadUpcomingAsync(request.ClinicId, request.LeadId, ct);
        if (upcoming.Count > 0) throw new LeadAlreadyBookedException(ToUpcoming(upcoming[0], tz));

        var check = await _availability.CheckSlotAsync(request.ClinicId, request.ProcedureId, request.ScheduledStart, request.ScheduledEnd, ct: ct);
        if (check.Error is not null) throw new SlotUnavailableException(check.Error);

        var appointment = await CreateCoreAsync(request with { ScheduledEnd = check.End }, ct);
        await tx.CommitAsync(ct);
        await NotifyChangedAsync(request.ClinicId, appointment, "created", ct); // only once the row is committed and visible
        return appointment;
    }

    /// <summary>Tells the clinic's open calendars to re-fetch (best-effort SignalR) AND — for created/rescheduled/canceled — creates
    /// the matching APPOINTMENT_* notification (see INotificationService; "status_changed" covers attended/no_show/confirmed, which
    /// aren't in the notification list). Called only once the appointment row is actually committed — never on a mere request.</summary>
    private async Task NotifyChangedAsync(Guid clinicId, AppointmentResponse appointment, string change, CancellationToken ct)
    {
        try { await _notifier.AppointmentChangedAsync(clinicId, appointment.Id, change, ct); }
        catch { /* best effort */ }

        // Only AFTER the appointment change is already committed — TriggerAppointmentSyncAsync itself decides
        // whether "change" maps to a real external-calendar operation (created/rescheduled/canceled only) and
        // never throws, so a calendar problem can never affect the appointment operation that already succeeded.
        await _calendarSync.TriggerAppointmentSyncAsync(clinicId, appointment, change, ct);

        var (type, title) = change switch
        {
            "created" => (NotificationType.AppointmentBooked, "Appointment booked"),
            "rescheduled" => (NotificationType.AppointmentRescheduled, "Appointment rescheduled"),
            "canceled" => (NotificationType.AppointmentCancelled, "Appointment canceled"),
            _ => (null, null)
        };
        if (type is null) return;

        // A [[time:…]] marker, not text: notifications.js shows it in each viewer's own timezone (SculptTime.expand).
        var whenLabel = $"[[time:{appointment.ScheduledStart.ToUniversalTime():yyyy-MM-dd'T'HH:mm:ss'Z'}]]";
        var who = string.IsNullOrWhiteSpace(appointment.LeadFullName) ? "A patient" : appointment.LeadFullName!;
        var what = string.IsNullOrWhiteSpace(appointment.ProcedureName) ? "a consultation" : appointment.ProcedureName!;
        var verb = change switch { "created" => "booked", "rescheduled" => "moved to", _ => "canceled for" };

        await _notifications.CreateAsync(clinicId, type, title!,
            $"{who} — {what} {verb} {whenLabel}.",
            leadId: appointment.LeadId, appointmentId: appointment.Id,
            link: $"/dashboard/appointments/{appointment.Id}", ct: ct);
    }

    public async Task<AppointmentResponse> CreateAsync(CreateAppointmentRequest request, CancellationToken ct = default)
    {
        var appointment = await CreateCoreAsync(request, ct);
        await NotifyChangedAsync(request.ClinicId, appointment, "created", ct);
        return appointment;
    }

    private async Task<AppointmentResponse> CreateCoreAsync(CreateAppointmentRequest request, CancellationToken ct)
    {
        // A new booking may only reference one of this clinic's ACTIVE procedures.
        if (request.ProcedureId is { } procedureId)
        {
            await _procedures.EnsureUsableAsync(request.ClinicId, procedureId, ct);
        }

        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            ClinicId = request.ClinicId,
            LeadId = request.LeadId,
            ProcedureId = request.ProcedureId,
            AppointmentType = string.IsNullOrWhiteSpace(request.AppointmentType) ? "consultation" : request.AppointmentType,
            Status = AppointmentStatus.Booked,
            // Npgsql only stores UTC-offset values; callers (n8n) send the clinic-local offset returned by get_available_slots.
            ScheduledStart = request.ScheduledStart.ToUniversalTime(),
            ScheduledEnd = request.ScheduledEnd?.ToUniversalTime(),
            LocationType = request.LocationType,
            LocationName = request.LocationName,
            Notes = request.Notes,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _appointments.Add(appointment);

        var lead = await _leads.GetAsync(request.ClinicId, request.LeadId, ct);
        if (lead is not null)
        {
            lead.Status = LeadStatus.ConsultationBooked;
            lead.UpdatedAt = DateTimeOffset.UtcNow;
        }

        _events.Log(request.ClinicId, EventTypes.ConsultationBooked, leadId: request.LeadId, appointmentId: appointment.Id);

        await _unitOfWork.SaveChangesAsync(ct);

        return await ToResponseAsync(appointment, ct);
    }

    public async Task<AppointmentResponse?> GetByIdAsync(Guid clinicId, Guid id, CancellationToken ct = default)
    {
        var appointment = await _appointments.GetWithDetailsAsync(clinicId, id, ct);
        return appointment is null ? null : AppointmentMapper.ToResponse(appointment);
    }

    public async Task<AppointmentResponse?> UpdateStatusAsync(Guid clinicId, Guid id, string status, CancellationToken ct = default)
    {
        if (!AppointmentStatus.All.Contains(status))
        {
            throw new ArgumentException($"Invalid appointment status '{status}'.", nameof(status));
        }

        var appointment = await _appointments.GetAsync(clinicId, id, ct);
        if (appointment is null) return null;

        appointment.Status = status;
        appointment.UpdatedAt = DateTimeOffset.UtcNow;

        var lead = await _leads.GetByIdAsync(appointment.LeadId, ct);
        if (lead is not null)
        {
            if (status == AppointmentStatus.Attended)
            {
                lead.Status = LeadStatus.ConsultationAttended;
                _events.Log(clinicId, EventTypes.ConsultationAttended, leadId: lead.Id, appointmentId: appointment.Id);
            }
            else if (status == AppointmentStatus.NoShow)
            {
                lead.Status = LeadStatus.NoShow;
                _events.Log(clinicId, EventTypes.ConsultationNoShow, leadId: lead.Id, appointmentId: appointment.Id);
            }
            lead.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _unitOfWork.SaveChangesAsync(ct);
        var updateResponse = await ToResponseAsync(appointment, ct);
        await NotifyChangedAsync(clinicId, updateResponse, status == AppointmentStatus.Canceled ? "canceled" : "status_changed", ct);
        return updateResponse;
    }

    public async Task<AppointmentResponse?> RescheduleAsync(
        Guid clinicId, Guid leadId, Guid? id, DateTimeOffset newStart, DateTimeOffset? newEnd, string? reason, CancellationToken ct = default)
    {
        await using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        await LockClinicAsync(clinicId, ct);

        // The appointment must belong to this clinic AND this lead: the AI only ever acts on the patient it is talking to.
        var targetId = await ResolveTargetAsync(clinicId, leadId, id, ct);
        if (targetId is null) return null;
        var appointment = await _appointments.GetForLeadAsync(clinicId, leadId, targetId.Value, ct);
        if (appointment is null) return null;

        if (appointment.Status != AppointmentStatus.Booked && appointment.Status != AppointmentStatus.Confirmed)
        {
            throw new ArgumentException($"This appointment is {appointment.Status} and can't be rescheduled.");
        }
        if (appointment.ScheduledStart <= DateTimeOffset.UtcNow)
        {
            throw new ArgumentException("This appointment is already in the past and can't be rescheduled. Book a new one instead.");
        }

        // A procedure that was deactivated since booking can't be newly booked, but must not trap the patient in an old time:
        // fall back to the clinic's default duration for it.
        var procedureId = await ActiveProcedureOrNullAsync(appointment.ProcedureId, ct);

        var check = await _availability.CheckSlotAsync(clinicId, procedureId, newStart, newEnd, excludeAppointmentId: appointment.Id, ct: ct);
        if (check.Error is not null) throw new SlotUnavailableException(check.Error);

        appointment.ScheduledStart = newStart.ToUniversalTime();
        appointment.ScheduledEnd = check.End.ToUniversalTime();
        // A rescheduled time isn't "confirmed" again until the clinic/staff confirms it — same
        // status a brand-new booking starts at.
        appointment.Status = AppointmentStatus.Booked;
        appointment.UpdatedAt = DateTimeOffset.UtcNow;

        _events.Log(clinicId, EventTypes.ConsultationRescheduled, leadId: appointment.LeadId, appointmentId: appointment.Id,
            metadataJson: JsonSerializer.Serialize(new { reason }));

        await _unitOfWork.SaveChangesAsync(ct);
        var response = await ToResponseAsync(appointment, ct);
        await tx.CommitAsync(ct);
        await NotifyChangedAsync(clinicId, response, "rescheduled", ct); // after the transaction commits
        return response;
    }

    public async Task<AppointmentResponse?> CancelAsync(Guid clinicId, Guid leadId, Guid? id, string? reason, CancellationToken ct = default)
    {
        var targetId = await ResolveTargetAsync(clinicId, leadId, id, ct);
        if (targetId is null) return null;
        var appointment = await _appointments.GetForLeadAsync(clinicId, leadId, targetId.Value, ct);
        if (appointment is null) return null;

        if (appointment.Status == AppointmentStatus.Canceled) return await ToResponseAsync(appointment, ct); // already done

        if (appointment.Status != AppointmentStatus.Booked && appointment.Status != AppointmentStatus.Confirmed)
        {
            throw new ArgumentException($"This appointment is {appointment.Status} and can't be canceled.");
        }

        appointment.Status = AppointmentStatus.Canceled;
        appointment.UpdatedAt = DateTimeOffset.UtcNow;

        _events.Log(clinicId, EventTypes.ConsultationCancelled, leadId: appointment.LeadId, appointmentId: appointment.Id,
            metadataJson: JsonSerializer.Serialize(new { reason }));

        await _unitOfWork.SaveChangesAsync(ct);
        var cancelResponse = await ToResponseAsync(appointment, ct);
        await NotifyChangedAsync(clinicId, cancelResponse, "canceled", ct);
        return cancelResponse;
    }

    public async Task<PatientBookingContext?> GetBookingContextAsync(Guid clinicId, Guid leadId, CancellationToken ct = default)
    {
        // A leadId from another clinic (or a made-up one) must look exactly like "not found" — never return its appointments.
        if (!await _leads.ExistsAsync(clinicId, leadId, ct)) return null;

        var (tz, upcoming) = await LoadUpcomingAsync(clinicId, leadId, ct);
        var existing = upcoming.Select(a => ToUpcoming(a, tz)).ToList();
        var blocked = existing.Count > 0;
        return new PatientBookingContext(blocked, !blocked, blocked ? "existing_upcoming_appointment" : null, existing);
    }

    public async Task<ScheduleOutcome> ScheduleAsync(Guid clinicId, Guid leadId, ScheduleConsultationRequest request, CancellationToken ct = default)
    {
        // The current state always comes from the database (never from what the AI remembers). Null = the lead isn't in this clinic.
        var context = await GetBookingContextAsync(clinicId, leadId, ct);
        if (context is null) return new ScheduleOutcome("none", "LEAD_NOT_FOUND", "Lead not found for this clinic.");

        var tz = await GetTimeZoneAsync(clinicId, ct);
        var requestedLabel = LabelFor(TimeZoneInfo.ConvertTime(request.ScheduledStart, tz));
        var existing = context.ExistingUpcomingAppointments;

        try
        {
            // 1) No upcoming appointment → create. BookAvailableSlotAsync still re-checks the slot and the duplicate guard under the lock,
            //    so a booking that landed a moment ago surfaces as LeadAlreadyBookedException below.
            if (!context.HasUpcomingAppointment)
            {
                var created = await BookAvailableSlotAsync(new CreateAppointmentRequest(
                    clinicId, leadId, request.ProcedureId,
                    string.IsNullOrWhiteSpace(request.AppointmentType) ? "consultation" : request.AppointmentType,
                    request.ScheduledStart, null, null, null, request.Notes), ct);
                return new ScheduleOutcome("created", "BOOKED", "Appointment booked.",
                    Appointment: await ToLocalAsync(clinicId, leadId, created.Id, ct), RequestedLabel: requestedLabel);
            }

            // 2) The patient already has an upcoming appointment. Which one would be replaced?
            // Exactly ONE upcoming appointment: the backend selects it itself and appointmentId is ignored completely (the AI's ids go
            // stale after any booking/cancel). Two or more: an appointmentId is required and must be one of the CURRENT upcoming ones,
            // otherwise nothing is mutated.
            UpcomingAppointmentResponse? target = null;
            if (existing.Count == 1)
            {
                target = existing[0];
            }
            else if (request.AppointmentId is { } wanted)
            {
                target = existing.FirstOrDefault(e => e.Id == wanted);
                if (target is null)
                {
                    return new ScheduleOutcome("none", "INVALID_REQUEST",
                        "That appointment is not one of this patient's current upcoming appointments.", Existing: existing, RequestedLabel: requestedLabel);
                }
            }

            // Read-only pre-check, so the patient is never asked to confirm a move to a time that cannot work.
            var check = await _availability.CheckSlotAsync(clinicId, await ActiveProcedureOrNullAsync(target?.ProcedureId ?? request.ProcedureId, ct),
                request.ScheduledStart, null, excludeAppointmentId: target?.Id, ct: ct);
            if (check.Error is not null)
            {
                return new ScheduleOutcome("none", "SLOT_UNAVAILABLE", check.Error, Existing: existing, RequestedLabel: requestedLabel);
            }

            // 3) No explicit consent → change NOTHING. Asking about another date is not permission to move the current appointment.
            if (!request.ConfirmReplaceExisting)
            {
                return new ScheduleOutcome("none", "CONFIRMATION_REQUIRED",
                    "Patient already has an upcoming appointment; nothing was changed.", Existing: existing, RequestedLabel: requestedLabel);
            }

            // 4) Consent given. With several upcoming appointments the patient must say which one.
            if (target is null)
            {
                return new ScheduleOutcome("none", "MULTIPLE_UPCOMING_APPOINTMENTS",
                    "Patient has more than one upcoming appointment; it is unclear which to move.", Existing: existing, RequestedLabel: requestedLabel);
            }

            var moved = await RescheduleAsync(clinicId, leadId, target.Id, request.ScheduledStart, null, request.Reason, ct);
            if (moved is null)
            {
                return new ScheduleOutcome("none", "INVALID_REQUEST", "The appointment could not be found for this patient.", RequestedLabel: requestedLabel);
            }
            return new ScheduleOutcome("rescheduled", "RESCHEDULED", "Appointment rescheduled.",
                Appointment: await ToLocalAsync(clinicId, leadId, moved.Id, ct), PreviousAppointment: target, RequestedLabel: requestedLabel);
        }
        catch (LeadAlreadyBookedException ex)
        {
            // Another booking landed between the state read and the locked check: same answer as "already has one".
            return new ScheduleOutcome("none", "CONFIRMATION_REQUIRED", "Patient already has an upcoming appointment; nothing was changed.",
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

    private async Task<TimeZoneInfo> GetTimeZoneAsync(Guid clinicId, CancellationToken ct)
    {
        var clinic = await _clinics.GetReadOnlyAsync(clinicId, ct);
        return clinic is null ? TimeZoneInfo.Utc : ResolveTimeZone(clinic);
    }

    /// <summary>A procedure id only when that procedure is still active — a deactivated one falls back to the clinic's default duration.</summary>
    private async Task<Guid?> ActiveProcedureOrNullAsync(Guid? procedureId, CancellationToken ct)
    {
        if (procedureId is not { } id) return null;
        return await _procedureRows.IsActiveAsync(id, ct) ? id : null;
    }

    private async Task<UpcomingAppointmentResponse?> ToLocalAsync(Guid clinicId, Guid leadId, Guid appointmentId, CancellationToken ct)
    {
        var (tz, items) = await LoadUpcomingAsync(clinicId, leadId, ct);
        var a = items.FirstOrDefault(x => x.Id == appointmentId);
        return a is null ? null : ToUpcoming(a, tz);
    }

    private static string LabelFor(DateTimeOffset local)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return $"{local.DayOfWeek}, {local.ToString("MMM d", inv)} at {local.ToString("h:mm tt", inv)}";
    }

    public async Task<CancelOutcome> CancelConsultationAsync(Guid clinicId, Guid leadId, Guid? id, string? reason, CancellationToken ct = default)
    {
        try
        {
            // Same rule as schedule_consultation: no upcoming → nothing to cancel; exactly ONE → the backend selects it and any appointmentId
            // is ignored (the AI's ids go stale); two or more → the id is required and must be a CURRENT upcoming appointment.
            var context = await GetBookingContextAsync(clinicId, leadId, ct);
            var existing = context?.ExistingUpcomingAppointments;
            if (existing is null || existing.Count == 0)
            {
                return new CancelOutcome("none", "NO_UPCOMING_APPOINTMENT", "This patient has no upcoming appointment to cancel (it may already be canceled).");
            }
            if (existing.Count > 1 && id is { } wanted && !existing.Any(e => e.Id == wanted))
            {
                return new CancelOutcome("none", "INVALID_REQUEST",
                    "That appointment is not one of this patient's current upcoming appointments.", Existing: existing);
            }

            var canceled = await CancelAsync(clinicId, leadId, existing.Count == 1 ? null : id, reason, ct);
            if (canceled is null)
            {
                return new CancelOutcome("none", "NO_UPCOMING_APPOINTMENT", "This patient has no upcoming appointment to cancel (it may already be canceled).");
            }

            // CancelAsync returns the stored (UTC) appointment; report it the way the patient should hear it — clinic-local.
            var tz = await GetTimeZoneAsync(clinicId, ct);
            var entity = await _appointments.GetWithProcedureReadOnlyAsync(canceled.Id, ct)
                ?? throw new InvalidOperationException("Canceled appointment not found.");
            return new CancelOutcome("canceled", "CANCELED", "Appointment canceled.", Appointment: ToUpcoming(entity, tz));
        }
        catch (MultipleUpcomingAppointmentsException ex)
        {
            return new CancelOutcome("none", "MULTIPLE_UPCOMING_APPOINTMENTS", ex.Message, Existing: ex.Appointments);
        }
        catch (ArgumentException ex)
        {
            return new CancelOutcome("none", "INVALID_REQUEST", ex.Message);
        }
    }

    public async Task<UpcomingAppointmentsResponse> GetUpcomingForLeadAsync(Guid clinicId, Guid leadId, CancellationToken ct = default)
    {
        var (tz, items) = await LoadUpcomingAsync(clinicId, leadId, ct);
        return new UpcomingAppointmentsResponse(tz.Id, items.Select(a => ToUpcoming(a, tz)).ToList());
    }

    /// <summary>The appointment a reschedule/cancel acts on: the given id, or — when the AI sends none — the lead's single upcoming
    /// appointment. Null when the lead has none; several upcoming ones (only staff can create them) need an explicit id.</summary>
    private async Task<Guid?> ResolveTargetAsync(Guid clinicId, Guid leadId, Guid? id, CancellationToken ct)
    {
        if (id is not null) return id;

        var (tz, upcoming) = await LoadUpcomingAsync(clinicId, leadId, ct);
        if (upcoming.Count == 0) return null;
        if (upcoming.Count > 1) throw new MultipleUpcomingAppointmentsException(upcoming.Select(a => ToUpcoming(a, tz)).ToList());
        return upcoming[0].Id;
    }

    /// <summary>The lead's future booked/confirmed appointments, soonest first (canceled, rescheduled, attended and no-show never
    /// count as "upcoming"), plus the clinic's timezone for wording them.</summary>
    private async Task<(TimeZoneInfo Tz, List<Appointment> Items)> LoadUpcomingAsync(Guid clinicId, Guid leadId, CancellationToken ct)
    {
        var clinic = await _clinics.GetReadOnlyAsync(clinicId, ct);
        var tz = clinic is null ? TimeZoneInfo.Utc : ResolveTimeZone(clinic);
        var now = DateTimeOffset.UtcNow;

        var items = (await _appointments.ListUpcomingForLeadAsync(clinicId, leadId, now, ct)).ToList();
        return (tz, items);
    }

    private static UpcomingAppointmentResponse ToUpcoming(Appointment a, TimeZoneInfo tz)
    {
        var start = TimeZoneInfo.ConvertTime(a.ScheduledStart, tz);
        DateTimeOffset? end = a.ScheduledEnd is null ? null : TimeZoneInfo.ConvertTime(a.ScheduledEnd.Value, tz);
        return new UpcomingAppointmentResponse(
            a.Id, a.Status, a.AppointmentType, a.ProcedureId, a.Procedure?.Name, start, end,
            start.ToString("yyyy-MM-dd"), start.ToString("HH:mm"),
            LabelFor(start));
    }

    /// <summary>Serializes bookings per clinic until the surrounding transaction ends.</summary>
    private Task LockClinicAsync(Guid clinicId, CancellationToken ct) => _appointments.LockClinicForBookingAsync(clinicId, ct);

    public async Task<(IReadOnlyList<AppointmentResponse> Items, int TotalCount)> ListAsync(
        Guid clinicId, string? status, DateTimeOffset? from, DateTimeOffset? to, int skip, int take, CancellationToken ct = default)
    {
        var (items, totalCount) = await _appointments.ListAsync(clinicId, status, from, to, skip, take, ct);

        return (items.Select(AppointmentMapper.ToResponse).ToList(), totalCount);
    }

    public async Task<CalendarMonthResponse> GetCalendarMonthAsync(Guid clinicId, int year, int month, TimeZoneInfo? displayTimeZone = null, CancellationToken ct = default)
    {
        var clinic = await _clinics.GetReadOnlyAsync(clinicId, ct);
        var clinicTz = clinic is null ? TimeZoneInfo.Utc : ResolveTimeZone(clinic);
        var tz = displayTimeZone ?? clinicTz;

        var firstOfMonth = new DateOnly(year, month, 1);
        // Sunday-start weeks (no existing calendar convention in this project to match yet). Pad the grid with
        // whole leading/trailing weeks so every row is a complete Sun–Sat week, same as any standard month view.
        var gridStart = firstOfMonth.AddDays(-(int)firstOfMonth.DayOfWeek);
        var lastOfMonth = firstOfMonth.AddMonths(1).AddDays(-1);
        var gridEnd = lastOfMonth.AddDays(6 - (int)lastOfMonth.DayOfWeek);

        // The grid's local boundaries, converted to UTC — this is the ONLY date range queried, so a month never
        // pulls in the whole appointments table (see IAppointmentService's remarks).
        var startUtc = LocalMidnightToUtc(gridStart, tz);
        var endUtc = LocalMidnightToUtc(gridEnd.AddDays(1), tz); // exclusive upper bound

        var appointments = await _appointments.ListStartingBetweenAsync(clinicId, startUtc, endUtc, ct);

        var items = appointments.Select(a =>
        {
            var local = TimeZoneInfo.ConvertTime(a.ScheduledStart, tz);
            return new CalendarAppointmentResponse(
                a.Id, a.LeadId, a.Lead?.FullName, a.ProcedureId, a.Procedure?.Name, a.Status,
                a.ScheduledStart, a.ScheduledEnd,
                local.ToString("yyyy-MM-dd"), local.ToString("HH:mm"));
        }).ToList();

        var needsOutcome = await _appointments.CountNeedingOutcomeAsync(clinicId, DateTimeOffset.UtcNow, ct);

        return new CalendarMonthResponse(year, month, tz.Id, clinicTz.Id, gridStart.ToString("yyyy-MM-dd"), gridEnd.ToString("yyyy-MM-dd"), items, needsOutcome);
    }

    /// <summary>Local midnight on <paramref name="date"/>, in <paramref name="tz"/>, as UTC.</summary>
    private static DateTimeOffset LocalMidnightToUtc(DateOnly date, TimeZoneInfo tz)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, tz.GetUtcOffset(local)).ToUniversalTime();
    }

    private async Task<AppointmentResponse> ToResponseAsync(Appointment a, CancellationToken ct)
    {
        await _appointments.LoadDetailsAsync(a, ct);
        return AppointmentMapper.ToResponse(a);
    }
}
