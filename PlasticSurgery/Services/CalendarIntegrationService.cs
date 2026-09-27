using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public class CalendarIntegrationService : ICalendarIntegrationService
{
    private readonly ApplicationDbContext _db;
    private readonly ICalendarSyncNotifier _notifier;
    private readonly INotificationService _notifications;

    public CalendarIntegrationService(ApplicationDbContext db, ICalendarSyncNotifier notifier, INotificationService notifications)
    {
        _db = db;
        _notifier = notifier;
        _notifications = notifications;
    }

    public async Task<IReadOnlyList<CalendarIntegrationResponse>> ListAsync(Guid clinicId, CancellationToken ct = default)
    {
        var existing = await _db.CalendarIntegrations
            .Include(c => c.Calendars)
            .Where(c => c.ClinicId == clinicId)
            .ToDictionaryAsync(c => c.Provider, ct);

        return CalendarProvider.All
            .Select(provider => existing.TryGetValue(provider, out var row)
                ? ToResponse(row)
                : new CalendarIntegrationResponse(
                    Guid.Empty, clinicId, provider, CalendarIntegrationStatus.Disconnected,
                    null, null, null, false, true, null, null, Array.Empty<CalendarCalendarOption>()))
            .ToList();
    }

    public async Task<CalendarIntegrationResponse> RequestConnectAsync(Guid clinicId, string provider, CancellationToken ct = default)
    {
        EnsureKnownProvider(provider);
        var row = await GetOrCreateAsync(clinicId, provider, ct);

        row.Status = CalendarIntegrationStatus.Pending;
        row.LastProblemMessage = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _notifier.NotifyConnectAsync(new CalendarConnectTriggerPayload(
            clinicId, row.Id, provider, "connect", ExternalConnectionRef: null), ct);

        return ToResponse(row);
    }

    public async Task RequestRefreshCalendarsAsync(Guid clinicId, string provider, CancellationToken ct = default)
    {
        EnsureKnownProvider(provider);
        var row = await _db.CalendarIntegrations.FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.Provider == provider, ct);
        if (row is null || row.Status != CalendarIntegrationStatus.Connected || string.IsNullOrWhiteSpace(row.ExternalConnectionRef))
        {
            throw new ArgumentException($"{provider} is not connected for this clinic yet.");
        }

        await _notifier.NotifyConnectAsync(new CalendarConnectTriggerPayload(
            clinicId, row.Id, provider, "refresh_calendars", row.ExternalConnectionRef), ct);
    }

    public async Task<CalendarIntegrationResponse> SelectCalendarAsync(
        Guid clinicId, string provider, string externalCalendarId, CancellationToken ct = default)
    {
        EnsureKnownProvider(provider);
        var row = await _db.CalendarIntegrations.Include(c => c.Calendars)
            .FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.Provider == provider, ct);
        if (row is null) throw new ArgumentException($"{provider} is not connected for this clinic.");

        var calendar = row.Calendars.FirstOrDefault(c => c.ExternalCalendarId == externalCalendarId);
        if (calendar is null)
        {
            throw new ArgumentException("That calendar is not in this account's list — refresh calendars and try again.");
        }

        row.SelectedCalendarId = calendar.ExternalCalendarId;
        row.SelectedCalendarName = calendar.Name;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return ToResponse(row);
    }

    public async Task<CalendarIntegrationResponse> SetSyncEnabledAsync(Guid clinicId, string provider, bool enabled, CancellationToken ct = default)
    {
        EnsureKnownProvider(provider);
        var row = await _db.CalendarIntegrations.Include(c => c.Calendars)
            .FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.Provider == provider, ct);
        if (row is null) throw new ArgumentException($"{provider} is not connected for this clinic.");
        if (enabled && string.IsNullOrWhiteSpace(row.SelectedCalendarId))
        {
            throw new ArgumentException("Choose a calendar before turning sync on.");
        }

        row.SyncEnabled = enabled;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return ToResponse(row);
    }

    public async Task DisconnectAsync(Guid clinicId, string provider, CancellationToken ct = default)
    {
        EnsureKnownProvider(provider);
        var row = await _db.CalendarIntegrations.Include(c => c.Calendars)
            .FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.Provider == provider, ct);
        if (row is null) return; // already disconnected — nothing to do

        var oldRef = row.ExternalConnectionRef;
        row.Status = CalendarIntegrationStatus.Disconnected;
        row.ExternalConnectionRef = null;
        row.AccountDisplayName = null;
        row.SelectedCalendarId = null;
        row.SelectedCalendarName = null;
        row.SyncEnabled = false;
        row.IsHealthy = true;
        row.LastProblemMessage = null;
        _db.CalendarIntegrationCalendars.RemoveRange(row.Calendars);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        if (!string.IsNullOrWhiteSpace(oldRef))
        {
            await _notifier.NotifyConnectAsync(new CalendarConnectTriggerPayload(clinicId, row.Id, provider, "disconnect", oldRef), ct);
        }
    }

    public async Task ApplyConnectCallbackAsync(CalendarConnectCallbackRequest request, CancellationToken ct = default)
    {
        var row = await _db.CalendarIntegrations.Include(c => c.Calendars)
            .FirstOrDefaultAsync(c => c.Id == request.CalendarIntegrationId, ct);
        // Never trust ClinicId from the callback body alone — the row it names must actually belong to it.
        if (row is null || row.ClinicId != request.ClinicId) return;

        if (!request.Success)
        {
            row.Status = CalendarIntegrationStatus.Error;
            row.LastProblemMessage = string.IsNullOrWhiteSpace(request.ErrorMessage)
                ? "Connection failed." : request.ErrorMessage;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            return;
        }

        row.Status = CalendarIntegrationStatus.Connected;
        row.ExternalConnectionRef = request.ExternalConnectionRef;
        if (!string.IsNullOrWhiteSpace(request.AccountDisplayName)) row.AccountDisplayName = request.AccountDisplayName;
        row.IsHealthy = true;
        row.LastProblemMessage = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;

        if (request.Calendars is not null)
        {
            _db.CalendarIntegrationCalendars.RemoveRange(row.Calendars);
            foreach (var c in request.Calendars)
            {
                _db.CalendarIntegrationCalendars.Add(new CalendarIntegrationCalendar
                {
                    Id = Guid.NewGuid(), CalendarIntegrationId = row.Id,
                    ExternalCalendarId = c.ExternalCalendarId, Name = c.Name, IsPrimary = c.IsPrimary,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
            // The previously selected calendar may no longer be in the refreshed list — clear it rather than
            // silently keep syncing to a calendar staff can no longer see or pick again.
            if (row.SelectedCalendarId is not null && request.Calendars.All(c => c.ExternalCalendarId != row.SelectedCalendarId))
            {
                row.SelectedCalendarId = null;
                row.SelectedCalendarName = null;
                row.SyncEnabled = false;
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task ApplySyncCallbackAsync(CalendarSyncCallbackRequest request, CancellationToken ct = default)
    {
        var sync = await _db.AppointmentCalendarSyncs.FirstOrDefaultAsync(
            s => s.AppointmentId == request.AppointmentId && s.CalendarIntegrationId == request.CalendarIntegrationId, ct);
        if (sync is null || sync.LastRequestId != request.RequestId) return; // unknown or superseded by a later request

        var integration = await _db.CalendarIntegrations.FirstOrDefaultAsync(
            c => c.Id == request.CalendarIntegrationId && c.ClinicId == request.ClinicId, ct);
        if (integration is null) return;

        var wasHealthy = integration.IsHealthy;
        var now = DateTimeOffset.UtcNow;

        if (request.Success)
        {
            sync.Status = sync.LastOperation == "cancel" ? AppointmentCalendarSyncStatus.Canceled : AppointmentCalendarSyncStatus.Synced;
            if (!string.IsNullOrWhiteSpace(request.ExternalEventId)) sync.ExternalEventId = request.ExternalEventId;
            sync.LastError = null;
            integration.IsHealthy = true;
            integration.LastProblemMessage = null;
            integration.LastSyncedAt = now;
        }
        else
        {
            sync.Status = AppointmentCalendarSyncStatus.Failed;
            sync.LastError = request.ErrorMessage;
            integration.IsHealthy = false;
            integration.LastProblemMessage = request.ErrorMessage ?? "A calendar sync failed.";
        }
        sync.UpdatedAt = now;
        integration.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        // Only on the transition into unhealthy — a connection that is already flagged, or one that only ever
        // succeeds, must not spam a notification on every appointment change.
        if (!request.Success && wasHealthy)
        {
            await _notifications.CreateAsync(integration.ClinicId, NotificationType.IntegrationUnhealthy,
                $"{ProviderLabel(integration.Provider)} calendar sync needs attention",
                integration.LastProblemMessage ?? "A calendar sync failed.",
                link: "/settings/calendar-integrations", ct: ct);
        }
    }

    public async Task TriggerAppointmentSyncAsync(Guid clinicId, AppointmentResponse appointment, string operation, CancellationToken ct = default)
    {
        try
        {
            var syncOp = operation switch
            {
                "created" => "create",
                "rescheduled" => "update",
                "canceled" => "cancel",
                _ => null // status_changed (attended/no_show/confirmed) — not synced
            };
            if (syncOp is null) return;

            var integrations = await _db.CalendarIntegrations
                .Where(c => c.ClinicId == clinicId && c.Status == CalendarIntegrationStatus.Connected && c.SyncEnabled
                            && c.SelectedCalendarId != null && c.ExternalConnectionRef != null)
                .ToListAsync(ct);
            if (integrations.Count == 0) return;

            var clinic = await _db.Clinics.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clinicId, ct);
            var timezone = clinic?.Timezone ?? "UTC";
            var title = $"{appointment.LeadFullName ?? "Patient"} — {appointment.ProcedureName ?? appointment.AppointmentType}";
            var description = BuildDescription(appointment);
            var link = $"/dashboard/appointments/{appointment.Id}";

            foreach (var integration in integrations)
            {
                var sync = await _db.AppointmentCalendarSyncs.FirstOrDefaultAsync(
                    s => s.AppointmentId == appointment.Id && s.CalendarIntegrationId == integration.Id, ct);

                // A cancelled sync row (its external event was already removed) stays cancelled forever — an
                // appointment that reaches this point again would be a fresh booking, which BookAvailableSlotAsync's
                // own one-upcoming-per-lead guard already prevents from reusing the same appointment row.
                if (sync is not null && sync.Status == AppointmentCalendarSyncStatus.Canceled && syncOp != "create") continue;

                string effectiveOp = syncOp;
                if (sync is null)
                {
                    sync = new AppointmentCalendarSync
                    {
                        Id = Guid.NewGuid(), AppointmentId = appointment.Id, CalendarIntegrationId = integration.Id,
                        Status = AppointmentCalendarSyncStatus.Pending, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
                    };
                    _db.AppointmentCalendarSyncs.Add(sync);
                }
                else if (effectiveOp == "create")
                {
                    // A sync row already exists for this appointment+calendar (a retried "created" notification,
                    // never expected in normal operation) — send it as an update instead of risking a duplicate.
                    effectiveOp = "update";
                }

                var requestId = Guid.NewGuid();
                sync.LastRequestId = requestId;
                sync.LastOperation = effectiveOp;
                sync.Status = AppointmentCalendarSyncStatus.Pending;
                sync.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);

                await _notifier.NotifySyncAsync(new CalendarSyncTriggerPayload(
                    clinicId, appointment.Id, integration.Id, integration.Provider, integration.ExternalConnectionRef!,
                    integration.SelectedCalendarId!, effectiveOp, requestId,
                    effectiveOp == "create" ? null : sync.ExternalEventId,
                    appointment.ScheduledStart, appointment.ScheduledEnd, timezone,
                    title, description, link), ct);
            }
        }
        catch
        {
            // Calendar sync is always secondary to the appointment operation that already succeeded — never
            // let a problem here surface to the caller (AppointmentService).
        }
    }

    // ------------------------------------------------------------------ helpers

    private static void EnsureKnownProvider(string provider)
    {
        if (!CalendarProvider.All.Contains(provider)) throw new ArgumentException($"Unknown calendar provider '{provider}'.");
    }

    private async Task<CalendarIntegration> GetOrCreateAsync(Guid clinicId, string provider, CancellationToken ct)
    {
        var row = await _db.CalendarIntegrations.Include(c => c.Calendars)
            .FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.Provider == provider, ct);
        if (row is not null) return row;

        var now = DateTimeOffset.UtcNow;
        row = new CalendarIntegration
        {
            Id = Guid.NewGuid(), ClinicId = clinicId, Provider = provider,
            Status = CalendarIntegrationStatus.Disconnected, CreatedAt = now, UpdatedAt = now
        };
        _db.CalendarIntegrations.Add(row);
        return row;
    }

    private static string BuildDescription(AppointmentResponse a)
    {
        // Deliberately conservative: no notes, no conversation history, no clinical detail — only what's already
        // shown on the SculptFlow calendar day-cell/drawer (see appointments-calendar.js).
        var parts = new List<string> { $"Status: {a.Status}" };
        if (!string.IsNullOrWhiteSpace(a.LocationName)) parts.Add($"Location: {a.LocationName}");
        return string.Join("\n", parts);
    }

    private static string ProviderLabel(string provider) => provider == CalendarProvider.Google ? "Google Calendar" : "Outlook Calendar";

    private static CalendarIntegrationResponse ToResponse(CalendarIntegration c) => new(
        c.Id, c.ClinicId, c.Provider, c.Status, c.AccountDisplayName, c.SelectedCalendarId, c.SelectedCalendarName,
        c.SyncEnabled, c.IsHealthy, c.LastProblemMessage, c.LastSyncedAt,
        c.Calendars.Select(x => new CalendarCalendarOption(x.ExternalCalendarId, x.Name, x.IsPrimary)).ToList());
}
