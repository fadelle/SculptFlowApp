using Omni.App.Entities.Dtos.Calendars;
using Omni.App.Entities.Requests.Calendars;
using Omni.App.Entities.Responses.Bookings;
using Omni.App.Entities.Responses.Calendars;

namespace Omni.App.Business.Contracts.Services.Calendars;

/// <summary>
/// Calendar Integrations (Settings → Calendar Integrations): one-way Omni → Google/Outlook booking sync.
/// Omni owns the OAuth relationship directly — connect, disconnect and calendar listing all talk to the
/// provider itself (see ICalendarProviderClient, CalendarOAuthController) and work even if n8n is unreachable. n8n's
/// only remaining role is executing the actual booking create/update/cancel call (TriggerBookingSyncAsync /
/// ICalendarSyncNotifier), using an access token Omni refreshed and hands over fresh each time.
/// </summary>
public interface ICalendarIntegrationService
{
    /// <summary>One row per known provider (CalendarProvider.All) for the client, always — a synthetic "disconnected"
    /// row (not persisted) for a provider never connected, same convention as IChannelIntegrationService.ListAsync.</summary>
    Task<IReadOnlyList<CalendarIntegrationResponse>> ListAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Marks the row Pending, right before CalendarOAuthController redirects the browser to the provider's
    /// consent screen. Idempotent — safe to call again if the client abandons one attempt and starts another.</summary>
    Task<CalendarIntegrationResponse> RequestConnectAsync(Guid clientId, string provider, CancellationToken ct = default);

    /// <summary>Called by CalendarOAuthController right after it has exchanged the provider's authorization code for
    /// tokens. Stores them, lists the account's calendars, and marks the row Connected/healthy.</summary>
    Task<CalendarIntegrationResponse> CompleteConnectAsync(
        Guid clientId, string provider, CalendarOAuthTokenResult tokens, string? accountEmail, CancellationToken ct = default);

    /// <summary>Called by CalendarOAuthController when the provider denies consent or the code exchange fails.
    /// Marks the row Error with the given message so the settings page can show it.</summary>
    Task<CalendarIntegrationResponse> FailConnectAsync(Guid clientId, string provider, string errorMessage, CancellationToken ct = default);

    /// <summary>Re-lists calendars for an already-connected account directly against the provider (e.g. the client
    /// created a new calendar). Throws ArgumentException if this provider isn't connected yet, or
    /// InvalidOperationException if the provider call itself fails (the integration is also marked unhealthy in
    /// that case).</summary>
    Task RequestRefreshCalendarsAsync(Guid clientId, string provider, CancellationToken ct = default);

    /// <summary>Staff picking which calendar Omni bookings sync to — a plain local write, validated
    /// against the cached list from the last connect/refresh. No provider call.</summary>
    Task<CalendarIntegrationResponse> SelectCalendarAsync(Guid clientId, string provider, string externalCalendarId, CancellationToken ct = default);

    Task<CalendarIntegrationResponse> SetSyncEnabledAsync(Guid clientId, string provider, bool enabled, CancellationToken ct = default);

    /// <summary>Clears the connection (status, account, selected calendar, cached list, tokens) and best-effort asks
    /// the provider to revoke them. Always clears local state even if the provider is unreachable. Existing
    /// BookingCalendarSync history is left alone (not deleted) — reconnecting later starts a fresh
    /// external_event_id trail rather than resurrecting stale references.</summary>
    Task DisconnectAsync(Guid clientId, string provider, CancellationToken ct = default);

    /// <summary>n8n's reply to a booking sync trigger. A callback whose RequestId doesn't match the
    /// BookingCalendarSync row's LastRequestId is a stale/superseded reply and is ignored.</summary>
    Task ApplySyncCallbackAsync(CalendarSyncCallbackRequest request, CancellationToken ct = default);

    /// <summary>Called from BookingService once a booking/reschedule/cancellation has already succeeded and been
    /// saved — never before. Refreshes each connected integration's access token if needed, then fires one sync
    /// trigger per connected-and-enabled calendar for this client. Best-effort: never throws, so a calendar problem
    /// can never affect the booking operation that triggered it.</summary>
    Task TriggerBookingSyncAsync(Guid clientId, BookingResponse booking, string operation, CancellationToken ct = default);
}
