using Omni.App.Entities.Requests.Calendars;

namespace Omni.App.Business.Contracts.HttpClients.Calendars;

/// <summary>Makes the one Google Calendar / Microsoft Graph call a booking change needs (the code engine of the calendar sync,
/// Calendars / SyncEngine = code) and answers what n8n's sync_callback would have sent: success with the event id, or, unlike
/// n8n, a failure with a short message. Null when the provider or operation has no route (n8n did nothing then). Never
/// throws for a provider problem.</summary>
public interface ICalendarSyncClient
{
    Task<CalendarSyncCallbackRequest?> SyncAsync(CalendarSyncTriggerPayload payload, CancellationToken ct = default);
}
