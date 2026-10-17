using Omni.App.Business.Contracts.HttpClients.Calendars;
using Omni.App.Business.Contracts.HttpClients.N8n;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Calendars;
using Omni.App.Business.Contracts.Services.Notifications;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.Calendars;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Calendars;
using Omni.App.Entities.Responses.Bookings;
using Omni.App.Entities.Responses.Calendars;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Calendars;
using Omni.App.Persistence.Contracts.Clients;

namespace Omni.App.Business.Services.Calendars;

public class CalendarIntegrationService : ICalendarIntegrationService
{
    // A token this close to expiring is refreshed proactively rather than handed to n8n or used for a calendar
    // listing call that might fail mid-flight.
    private TimeSpan TokenRefreshMargin => TimeSpan.FromMinutes(_config.IntegrationsTokenRefreshMarginMinutes);

    private readonly ICalendarIntegrationRepository _calendars;
    private readonly IClientRepository _clients;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICalendarSyncNotifier _notifier;
    private readonly INotificationService _notifications;
    private readonly IEnumerable<ICalendarProviderClient> _providerClients;
    private readonly IConfigManager _config;

    public CalendarIntegrationService(
        ICalendarIntegrationRepository calendars, IClientRepository clients, IUnitOfWork unitOfWork, ICalendarSyncNotifier notifier, INotificationService notifications,
        IEnumerable<ICalendarProviderClient> providerClients, IConfigManager config)
    {
        _config = config;
        _calendars = calendars;
        _clients = clients;
        _unitOfWork = unitOfWork;
        _notifier = notifier;
        _notifications = notifications;
        _providerClients = providerClients;
    }

    public async Task<IReadOnlyList<CalendarIntegrationResponse>> ListAsync(Guid clientId, CancellationToken ct = default)
    {
        var existing = (await _calendars.ListWithCalendarsReadOnlyAsync(clientId, ct)).ToDictionary(c => c.Provider);

        return CalendarProvider.All
            .Select(provider => existing.TryGetValue(provider, out var row)
                ? ToResponse(row)
                : new CalendarIntegrationResponse(
                    Guid.Empty, clientId, provider, CalendarIntegrationStatus.Disconnected,
                    null, null, null, false, true, null, null, Array.Empty<CalendarCalendarOption>()))
            .ToList();
    }

    public async Task<CalendarIntegrationResponse> RequestConnectAsync(Guid clientId, string provider, CancellationToken ct = default)
    {
        EnsureKnownProvider(provider);
        var row = await GetOrCreateAsync(clientId, provider, ct);

        row.Status = CalendarIntegrationStatus.Pending;
        row.LastProblemMessage = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        return ToResponse(row);
    }

    public async Task<CalendarIntegrationResponse> CompleteConnectAsync(
        Guid clientId, string provider, CalendarOAuthTokenResult tokens, string? accountEmail, CancellationToken ct = default)
    {
        EnsureKnownProvider(provider);
        var row = await GetOrCreateAsync(clientId, provider, ct);
        var client = ResolveProviderClient(provider);

        var calendars = await client.ListCalendarsAsync(tokens.AccessToken, ct);

        row.AccessToken = tokens.AccessToken;
        row.RefreshToken = tokens.RefreshToken ?? row.RefreshToken;
        row.TokenExpiresAt = tokens.ExpiresAt;
        row.AccountDisplayName = accountEmail ?? row.AccountDisplayName;
        row.Status = CalendarIntegrationStatus.Connected;
        row.IsHealthy = true;
        row.LastProblemMessage = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;

        _calendars.ReplaceCalendars(row, calendars.Select(c => new CalendarIntegrationCalendar
        {
            Id = Guid.NewGuid(), CalendarIntegrationId = row.Id,
            ExternalCalendarId = c.ExternalCalendarId, Name = c.Name, IsPrimary = c.IsPrimary,
            CreatedAt = DateTimeOffset.UtcNow
        }).ToList());
        // A previously selected calendar may no longer be in the refreshed list (reconnecting a different
        // account entirely, say) — clear it rather than silently keep pointing at a calendar members can't see anymore.
        if (row.SelectedCalendarId is not null && calendars.All(c => c.ExternalCalendarId != row.SelectedCalendarId))
        {
            row.SelectedCalendarId = null;
            row.SelectedCalendarName = null;
            row.SyncEnabled = false;
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return ToResponse(row);
    }

    public async Task<CalendarIntegrationResponse> FailConnectAsync(Guid clientId, string provider, string errorMessage, CancellationToken ct = default)
    {
        EnsureKnownProvider(provider);
        var row = await GetOrCreateAsync(clientId, provider, ct);
        row.Status = CalendarIntegrationStatus.Error;
        row.LastProblemMessage = errorMessage;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);
        return ToResponse(row);
    }

    public async Task RequestRefreshCalendarsAsync(Guid clientId, string provider, CancellationToken ct = default)
    {
        EnsureKnownProvider(provider);
        var row = await _calendars.GetWithCalendarsAsync(clientId, provider, ct);
        if (row is null || row.Status != CalendarIntegrationStatus.Connected)
        {
            throw new ArgumentException($"{provider} is not connected for this client yet.");
        }

        var client = ResolveProviderClient(provider);
        try
        {
            var accessToken = await EnsureFreshAccessTokenAsync(row, client, ct);
            var calendars = await client.ListCalendarsAsync(accessToken, ct);

            _calendars.ReplaceCalendars(row, calendars.Select(c => new CalendarIntegrationCalendar
            {
                Id = Guid.NewGuid(), CalendarIntegrationId = row.Id,
                ExternalCalendarId = c.ExternalCalendarId, Name = c.Name, IsPrimary = c.IsPrimary,
                CreatedAt = DateTimeOffset.UtcNow
            }).ToList());
            if (row.SelectedCalendarId is not null && calendars.All(c => c.ExternalCalendarId != row.SelectedCalendarId))
            {
                row.SelectedCalendarId = null;
                row.SelectedCalendarName = null;
                row.SyncEnabled = false;
            }
            row.IsHealthy = true;
            row.LastProblemMessage = null;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            row.IsHealthy = false;
            row.LastProblemMessage = ex.Message;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await _unitOfWork.SaveChangesAsync(ct);
            throw new InvalidOperationException($"Couldn't refresh {provider} calendars: {ex.Message}", ex);
        }
    }

    public async Task<CalendarIntegrationResponse> SelectCalendarAsync(
        Guid clientId, string provider, string externalCalendarId, CancellationToken ct = default)
    {
        EnsureKnownProvider(provider);
        var row = await _calendars.GetWithCalendarsAsync(clientId, provider, ct);
        if (row is null) throw new ArgumentException($"{provider} is not connected for this client.");

        var calendar = row.Calendars.FirstOrDefault(c => c.ExternalCalendarId == externalCalendarId);
        if (calendar is null)
        {
            throw new ArgumentException("That calendar is not in this account's list — refresh calendars and try again.");
        }

        row.SelectedCalendarId = calendar.ExternalCalendarId;
        row.SelectedCalendarName = calendar.Name;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        return ToResponse(row);
    }

    public async Task<CalendarIntegrationResponse> SetSyncEnabledAsync(Guid clientId, string provider, bool enabled, CancellationToken ct = default)
    {
        EnsureKnownProvider(provider);
        var row = await _calendars.GetWithCalendarsAsync(clientId, provider, ct);
        if (row is null) throw new ArgumentException($"{provider} is not connected for this client.");
        if (enabled && string.IsNullOrWhiteSpace(row.SelectedCalendarId))
        {
            throw new ArgumentException("Choose a calendar before turning sync on.");
        }

        row.SyncEnabled = enabled;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        return ToResponse(row);
    }

    public async Task DisconnectAsync(Guid clientId, string provider, CancellationToken ct = default)
    {
        EnsureKnownProvider(provider);
        var row = await _calendars.GetWithCalendarsAsync(clientId, provider, ct);
        if (row is null) return; // already disconnected — nothing to do

        if (!string.IsNullOrWhiteSpace(row.AccessToken) || !string.IsNullOrWhiteSpace(row.RefreshToken))
        {
            await ResolveProviderClient(provider).RevokeAsync(row.AccessToken, row.RefreshToken, ct);
        }

        row.Status = CalendarIntegrationStatus.Disconnected;
        row.ExternalConnectionRef = null;
        row.AccountDisplayName = null;
        row.SelectedCalendarId = null;
        row.SelectedCalendarName = null;
        row.SyncEnabled = false;
        row.IsHealthy = true;
        row.LastProblemMessage = null;
        row.AccessToken = null;
        row.RefreshToken = null;
        row.TokenExpiresAt = null;
        _calendars.RemoveCalendars(row);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task ApplySyncCallbackAsync(CalendarSyncCallbackRequest request, CancellationToken ct = default)
    {
        var sync = await _calendars.GetSyncAsync(request.BookingId, request.CalendarIntegrationId, ct);
        if (sync is null || sync.LastRequestId != request.RequestId) return; // unknown or superseded by a later request

        var integration = await _calendars.GetByIdAsync(request.ClientId, request.CalendarIntegrationId, ct);
        if (integration is null) return;

        var wasHealthy = integration.IsHealthy;
        var now = DateTimeOffset.UtcNow;

        if (request.Success)
        {
            sync.Status = sync.LastOperation == "cancel" ? BookingCalendarSyncStatus.Canceled : BookingCalendarSyncStatus.Synced;
            if (!string.IsNullOrWhiteSpace(request.ExternalEventId)) sync.ExternalEventId = request.ExternalEventId;
            sync.LastError = null;
            integration.IsHealthy = true;
            integration.LastProblemMessage = null;
            integration.LastSyncedAt = now;
        }
        else
        {
            sync.Status = BookingCalendarSyncStatus.Failed;
            sync.LastError = request.ErrorMessage;
            integration.IsHealthy = false;
            integration.LastProblemMessage = request.ErrorMessage ?? "A calendar sync failed.";
        }
        sync.UpdatedAt = now;
        integration.UpdatedAt = now;
        await _unitOfWork.SaveChangesAsync(ct);

        // Only on the transition into unhealthy — a connection that is already flagged, or one that only ever
        // succeeds, must not spam a notification on every booking change.
        if (!request.Success && wasHealthy)
        {
            await _notifications.CreateAsync(integration.ClientId, NotificationType.IntegrationUnhealthy,
                $"{ProviderLabel(integration.Provider)} calendar sync needs attention",
                integration.LastProblemMessage ?? "A calendar sync failed.",
                link: "/settings/calendar-integrations", ct: ct);
        }
    }

    public async Task TriggerBookingSyncAsync(Guid clientId, BookingResponse booking, string operation, CancellationToken ct = default)
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

            var integrations = await _calendars.ListSyncTargetsAsync(clientId, ct);
            if (integrations.Count == 0) return;

            var client = await _clients.GetReadOnlyAsync(clientId, ct);
            var timezone = client?.Timezone ?? "UTC";
            var title = $"{booking.ContactFullName ?? "Contact"} — {booking.ServiceName ?? booking.BookingType}";
            var description = BuildDescription(booking);
            var link = $"/dashboard/bookings/{booking.Id}";

            foreach (var integration in integrations)
            {
                string accessToken;
                try
                {
                    accessToken = await EnsureFreshAccessTokenAsync(integration, ResolveProviderClient(integration.Provider), ct);
                }
                catch
                {
                    // A token that can't be refreshed is a health problem for THIS integration only — skip it and
                    // let the others (if any) still sync; EnsureFreshAccessTokenAsync already marked it unhealthy.
                    continue;
                }

                var sync = await _calendars.GetSyncAsync(booking.Id, integration.Id, ct);

                // A cancelled sync row (its external event was already removed) stays cancelled forever — an
                // booking that reaches this point again would be a fresh booking, which BookAvailableSlotAsync's
                // own one-upcoming-per-contact guard already prevents from reusing the same booking row.
                if (sync is not null && sync.Status == BookingCalendarSyncStatus.Canceled && syncOp != "create") continue;

                string effectiveOp = syncOp;
                if (sync is null)
                {
                    sync = new BookingCalendarSync
                    {
                        Id = Guid.NewGuid(), BookingId = booking.Id, CalendarIntegrationId = integration.Id,
                        Status = BookingCalendarSyncStatus.Pending, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
                    };
                    _calendars.AddSync(sync);
                }
                else if (effectiveOp == "create")
                {
                    // A sync row already exists for this booking+calendar (a retried "created" notification,
                    // never expected in normal operation) — send it as an update instead of risking a duplicate.
                    effectiveOp = "update";
                }

                var requestId = Guid.NewGuid();
                sync.LastRequestId = requestId;
                sync.LastOperation = effectiveOp;
                sync.Status = BookingCalendarSyncStatus.Pending;
                sync.UpdatedAt = DateTimeOffset.UtcNow;
                await _unitOfWork.SaveChangesAsync(ct);

                await _notifier.NotifySyncAsync(new CalendarSyncTriggerPayload(
                    clientId, booking.Id, integration.Id, integration.Provider, accessToken,
                    integration.SelectedCalendarId!, effectiveOp, requestId,
                    effectiveOp == "create" ? null : sync.ExternalEventId,
                    booking.ScheduledStart, booking.ScheduledEnd, timezone,
                    title, description, link), ct);
            }
        }
        catch
        {
            // Calendar sync is always secondary to the booking operation that already succeeded — never
            // let a problem here surface to the caller (BookingService).
        }
    }

    // ------------------------------------------------------------------ helpers

    private static void EnsureKnownProvider(string provider)
    {
        if (!CalendarProvider.All.Contains(provider)) throw new ArgumentException($"Unknown calendar provider '{provider}'.");
    }

    private ICalendarProviderClient ResolveProviderClient(string provider) =>
        _providerClients.FirstOrDefault(c => c.Provider == provider)
        ?? throw new InvalidOperationException($"No calendar provider client registered for '{provider}'.");

    /// <summary>Refreshes and persists a new access token when the stored one is missing/near expiry, using the
    /// stored refresh token. Marks the integration unhealthy and rethrows if there is no refresh token to use or the
    /// provider rejects the refresh — callers must treat that as "this integration cannot sync right now".</summary>
    private async Task<string> EnsureFreshAccessTokenAsync(CalendarIntegration row, ICalendarProviderClient client, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(row.AccessToken) && row.TokenExpiresAt is { } expires
            && expires - DateTimeOffset.UtcNow > TokenRefreshMargin)
        {
            return row.AccessToken;
        }

        if (string.IsNullOrWhiteSpace(row.RefreshToken))
        {
            row.IsHealthy = false;
            row.LastProblemMessage = "This connection has no refresh token on file — reconnect it from Calendar Integrations.";
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await _unitOfWork.SaveChangesAsync(ct);
            throw new InvalidOperationException(row.LastProblemMessage);
        }

        try
        {
            var refreshed = await client.RefreshAccessTokenAsync(row.RefreshToken, ct);
            row.AccessToken = refreshed.AccessToken;
            row.RefreshToken = refreshed.RefreshToken ?? row.RefreshToken;
            row.TokenExpiresAt = refreshed.ExpiresAt;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await _unitOfWork.SaveChangesAsync(ct);
            return refreshed.AccessToken;
        }
        catch (Exception ex)
        {
            row.IsHealthy = false;
            row.LastProblemMessage = $"Token refresh failed — reconnect this calendar. ({ex.Message})";
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await _unitOfWork.SaveChangesAsync(ct);
            throw;
        }
    }

    private async Task<CalendarIntegration> GetOrCreateAsync(Guid clientId, string provider, CancellationToken ct)
    {
        var row = await _calendars.GetWithCalendarsAsync(clientId, provider, ct);
        if (row is not null) return row;

        var now = DateTimeOffset.UtcNow;
        row = new CalendarIntegration
        {
            Id = Guid.NewGuid(), ClientId = clientId, Provider = provider,
            Status = CalendarIntegrationStatus.Disconnected, CreatedAt = now, UpdatedAt = now
        };
        _calendars.Add(row);
        return row;
    }

    private static string BuildDescription(BookingResponse a)
    {
        // Deliberately conservative: no notes, no conversation history, no medical detail — only what's already
        // shown on the Omni calendar day-cell/drawer (see bookings-calendar.js).
        var parts = new List<string> { $"Status: {a.Status}" };
        if (!string.IsNullOrWhiteSpace(a.LocationName)) parts.Add($"Location: {a.LocationName}");
        return string.Join("\n", parts);
    }

    private static string ProviderLabel(string provider) => provider == CalendarProvider.Google ? "Google Calendar" : "Outlook Calendar";

    private static CalendarIntegrationResponse ToResponse(CalendarIntegration c) => new(
        c.Id, c.ClientId, c.Provider, c.Status, c.AccountDisplayName, c.SelectedCalendarId, c.SelectedCalendarName,
        c.SyncEnabled, c.IsHealthy, c.LastProblemMessage, c.LastSyncedAt,
        c.Calendars.Select(x => new CalendarCalendarOption(x.ExternalCalendarId, x.Name, x.IsPrimary)).ToList());
}
