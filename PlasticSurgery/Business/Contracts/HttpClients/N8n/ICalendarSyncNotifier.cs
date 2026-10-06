using System.Text.Json;
using PlasticSurgery.Entities.Requests.Calendars;

namespace PlasticSurgery.Business.Contracts.HttpClients.N8n;

/// <summary>The one remaining outbound call to n8n's dedicated Calendar Sync workflow: appointment create/update/
/// cancel. Connect/disconnect/list-calendars are handled entirely inside SculptFlow now (see ICalendarProviderClient,
/// CalendarOAuthController) — n8n only ever receives a fresh access token to make the one API call this trigger
/// asks for. Never throws — a calendar sync is always secondary to the SculptFlow operation it followed, which has
/// already succeeded and been saved by the time this is called.</summary>
public interface ICalendarSyncNotifier
{
    Task NotifySyncAsync(CalendarSyncTriggerPayload payload, CancellationToken ct = default);
}
