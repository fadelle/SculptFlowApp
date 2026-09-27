using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

/// <summary>
/// Calendar Integrations (Settings → Calendar Integrations): one-way SculptFlow → Google/Outlook appointment sync
/// through a dedicated n8n workflow. SculptFlow is the source of truth and has no Google/Outlook-specific code —
/// see ICalendarSyncNotifier's doc comment for the full trust boundary.
/// </summary>
public interface ICalendarIntegrationService
{
    /// <summary>One row per known provider (CalendarProvider.All) for the clinic, always — a synthetic "disconnected"
    /// row (not persisted) for a provider never connected, same convention as IChannelIntegrationService.ListAsync.</summary>
    Task<IReadOnlyList<CalendarIntegrationResponse>> ListAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Marks the row Pending and fires the "connect" trigger to n8n. The actual OAuth handshake and the
    /// resulting account/calendar list only land once ApplyConnectCallbackAsync is called.</summary>
    Task<CalendarIntegrationResponse> RequestConnectAsync(Guid clinicId, string provider, CancellationToken ct = default);

    /// <summary>Asks n8n to re-list calendars for an already-connected account (e.g. the clinic created a new
    /// calendar). Throws ArgumentException if this provider isn't connected yet.</summary>
    Task RequestRefreshCalendarsAsync(Guid clinicId, string provider, CancellationToken ct = default);

    /// <summary>Staff picking which calendar SculptFlow appointments sync to — a plain local write, validated
    /// against the cached list from the last connect/refresh. No n8n call.</summary>
    Task<CalendarIntegrationResponse> SelectCalendarAsync(Guid clinicId, string provider, string externalCalendarId, CancellationToken ct = default);

    Task<CalendarIntegrationResponse> SetSyncEnabledAsync(Guid clinicId, string provider, bool enabled, CancellationToken ct = default);

    /// <summary>Clears the connection (status, account, selected calendar, cached list) and best-effort tells n8n it
    /// may forget the stored credentials. Existing AppointmentCalendarSync history is left alone (not deleted) —
    /// reconnecting later starts a fresh external_event_id trail rather than resurrecting stale references.</summary>
    Task DisconnectAsync(Guid clinicId, string provider, CancellationToken ct = default);

    /// <summary>n8n's reply to a connect/refresh_calendars trigger. Ignored (logged, not an error) if the
    /// CalendarIntegrationId is unknown or belongs to a different clinic than ClinicId claims.</summary>
    Task ApplyConnectCallbackAsync(CalendarConnectCallbackRequest request, CancellationToken ct = default);

    /// <summary>n8n's reply to an appointment sync trigger. A callback whose RequestId doesn't match the
    /// AppointmentCalendarSync row's LastRequestId is a stale/superseded reply and is ignored.</summary>
    Task ApplySyncCallbackAsync(CalendarSyncCallbackRequest request, CancellationToken ct = default);

    /// <summary>Called from AppointmentService once a booking/reschedule/cancellation has already succeeded and been
    /// saved — never before. Fires one sync trigger per connected-and-enabled calendar for this clinic. Best-effort:
    /// never throws, so a calendar problem can never affect the appointment operation that triggered it.</summary>
    Task TriggerAppointmentSyncAsync(Guid clinicId, AppointmentResponse appointment, string operation, CancellationToken ct = default);
}
