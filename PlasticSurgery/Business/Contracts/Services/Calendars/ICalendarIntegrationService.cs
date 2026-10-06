using PlasticSurgery.Entities.Dtos.Calendars;
using PlasticSurgery.Entities.Requests.Calendars;
using PlasticSurgery.Entities.Responses.Appointments;
using PlasticSurgery.Entities.Responses.Calendars;

namespace PlasticSurgery.Business.Contracts.Services.Calendars;

/// <summary>
/// Calendar Integrations (Settings → Calendar Integrations): one-way SculptFlow → Google/Outlook appointment sync.
/// SculptFlow owns the OAuth relationship directly — connect, disconnect and calendar listing all talk to the
/// provider itself (see ICalendarProviderClient, CalendarOAuthController) and work even if n8n is unreachable. n8n's
/// only remaining role is executing the actual appointment create/update/cancel call (TriggerAppointmentSyncAsync /
/// ICalendarSyncNotifier), using an access token SculptFlow refreshed and hands over fresh each time.
/// </summary>
public interface ICalendarIntegrationService
{
    /// <summary>One row per known provider (CalendarProvider.All) for the clinic, always — a synthetic "disconnected"
    /// row (not persisted) for a provider never connected, same convention as IChannelIntegrationService.ListAsync.</summary>
    Task<IReadOnlyList<CalendarIntegrationResponse>> ListAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Marks the row Pending, right before CalendarOAuthController redirects the browser to the provider's
    /// consent screen. Idempotent — safe to call again if the clinic abandons one attempt and starts another.</summary>
    Task<CalendarIntegrationResponse> RequestConnectAsync(Guid clinicId, string provider, CancellationToken ct = default);

    /// <summary>Called by CalendarOAuthController right after it has exchanged the provider's authorization code for
    /// tokens. Stores them, lists the account's calendars, and marks the row Connected/healthy.</summary>
    Task<CalendarIntegrationResponse> CompleteConnectAsync(
        Guid clinicId, string provider, CalendarOAuthTokenResult tokens, string? accountEmail, CancellationToken ct = default);

    /// <summary>Called by CalendarOAuthController when the provider denies consent or the code exchange fails.
    /// Marks the row Error with the given message so the settings page can show it.</summary>
    Task<CalendarIntegrationResponse> FailConnectAsync(Guid clinicId, string provider, string errorMessage, CancellationToken ct = default);

    /// <summary>Re-lists calendars for an already-connected account directly against the provider (e.g. the clinic
    /// created a new calendar). Throws ArgumentException if this provider isn't connected yet, or
    /// InvalidOperationException if the provider call itself fails (the integration is also marked unhealthy in
    /// that case).</summary>
    Task RequestRefreshCalendarsAsync(Guid clinicId, string provider, CancellationToken ct = default);

    /// <summary>Staff picking which calendar SculptFlow appointments sync to — a plain local write, validated
    /// against the cached list from the last connect/refresh. No provider call.</summary>
    Task<CalendarIntegrationResponse> SelectCalendarAsync(Guid clinicId, string provider, string externalCalendarId, CancellationToken ct = default);

    Task<CalendarIntegrationResponse> SetSyncEnabledAsync(Guid clinicId, string provider, bool enabled, CancellationToken ct = default);

    /// <summary>Clears the connection (status, account, selected calendar, cached list, tokens) and best-effort asks
    /// the provider to revoke them. Always clears local state even if the provider is unreachable. Existing
    /// AppointmentCalendarSync history is left alone (not deleted) — reconnecting later starts a fresh
    /// external_event_id trail rather than resurrecting stale references.</summary>
    Task DisconnectAsync(Guid clinicId, string provider, CancellationToken ct = default);

    /// <summary>n8n's reply to an appointment sync trigger. A callback whose RequestId doesn't match the
    /// AppointmentCalendarSync row's LastRequestId is a stale/superseded reply and is ignored.</summary>
    Task ApplySyncCallbackAsync(CalendarSyncCallbackRequest request, CancellationToken ct = default);

    /// <summary>Called from AppointmentService once a booking/reschedule/cancellation has already succeeded and been
    /// saved — never before. Refreshes each connected integration's access token if needed, then fires one sync
    /// trigger per connected-and-enabled calendar for this clinic. Best-effort: never throws, so a calendar problem
    /// can never affect the appointment operation that triggered it.</summary>
    Task TriggerAppointmentSyncAsync(Guid clinicId, AppointmentResponse appointment, string operation, CancellationToken ct = default);
}
