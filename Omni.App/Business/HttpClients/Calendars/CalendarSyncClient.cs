using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Omni.App.Business.Contracts.HttpClients.Calendars;
using Omni.App.Common.Enums;
using Omni.App.Entities.Requests.Calendars;

namespace Omni.App.Business.HttpClients.Calendars;

/// <summary>
/// The calendar sync workflow (docs/n8n/omni-calendar-sync.json) as code: "provider" → "google_operation" /
/// "outlook_operation" → one Google Calendar or Microsoft Graph call → "sync_callback". Same calls as the n8n nodes: the
/// primary calendar (calendars/primary/events, me/events), POST to create, PATCH to update, DELETE to cancel, the
/// payload's access token as Bearer, and the event body below. Unlike n8n, a failed call is reported (success = false)
/// instead of never calling back.
/// </summary>
public class CalendarSyncClient : ICalendarSyncClient
{
    public const string GoogleEvents = "https://www.googleapis.com/calendar/v3/calendars/primary/events";
    public const string GraphEvents = "https://graph.microsoft.com/v1.0/me/events";

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ILogger<CalendarSyncClient> _logger;

    public CalendarSyncClient(HttpClient http, ILogger<CalendarSyncClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<CalendarSyncCallbackRequest?> SyncAsync(CalendarSyncTriggerPayload payload, CancellationToken ct = default)
    {
        var google = payload.Provider == CalendarProvider.Google;
        if (!google && payload.Provider != CalendarProvider.Outlook) return Unrouted(payload);

        var events = google ? GoogleEvents : GraphEvents;
        var (method, url, body) = payload.Operation switch
        {
            "create" => (HttpMethod.Post, events, Event(payload, google)),
            "update" => (HttpMethod.Patch, $"{events}/{payload.ExternalEventId}", Event(payload, google)),
            "cancel" => (HttpMethod.Delete, $"{events}/{payload.ExternalEventId}", (JsonObject?)null),
            _ => (null!, "", null)
        };
        if (method is null) return Unrouted(payload);

        var provider = google ? "Google Calendar" : "Outlook calendar"; // text-ok: stored as the connection's problem message, like the providers' own errors
        try
        {
            using var request = new HttpRequestMessage(method, url)
            {
                // n8n sent Content-Type: application/json on every call, DELETE included.
                Content = new StringContent(body?.ToJsonString() ?? "", Encoding.UTF8, "application/json")
            };
            if (body is null) request.Content.Headers.ContentLength = 0;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", payload.AccessToken);

            using var response = await _http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                // The body can name the calendar or echo the request, so it is only logged.
                _logger.LogWarning("Calendar sync {Operation} on {Provider} failed ({Status}) for integration {IntegrationId}: {Body}",
                    payload.Operation, payload.Provider, (int)response.StatusCode, payload.CalendarIntegrationId, text.Length > 300 ? text[..300] : text);
                return Callback(payload, false, null, $"{provider} answered HTTP {(int)response.StatusCode} to the {payload.Operation}."); // text-ok: stored problem message
            }

            // $json.id || the payload's externalEventId || null (a DELETE answers 204 without a body).
            return Callback(payload, true, CreatedId(text) ?? NullIfEmpty(payload.ExternalEventId), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Calendar sync {Operation} on {Provider} could not reach the provider for integration {IntegrationId}.",
                payload.Operation, payload.Provider, payload.CalendarIntegrationId);
            return Callback(payload, false, null, $"{provider} couldn't be reached for the {payload.Operation}."); // text-ok: stored problem message
        }
    }

    // summary / subject = eventTitle; description = [eventDescription, dashboardLink].filter(Boolean).join('\n\n');
    // start / end = { dateTime: the payload's value as n8n received it, timeZone: the payload's timezone }.
    private static JsonObject Event(CalendarSyncTriggerPayload payload, bool google)
    {
        var description = string.Join("\n\n", new[] { payload.EventDescription, payload.DashboardLink }.Where(p => !string.IsNullOrEmpty(p)));
        var start = new JsonObject { ["dateTime"] = JsonSerializer.SerializeToNode(payload.ScheduledStart, WebJson), ["timeZone"] = payload.Timezone };
        var end = new JsonObject { ["dateTime"] = JsonSerializer.SerializeToNode(payload.ScheduledEnd, WebJson), ["timeZone"] = payload.Timezone };
        return google
            ? new JsonObject { ["summary"] = payload.EventTitle, ["description"] = description, ["start"] = start, ["end"] = end }
            : new JsonObject
            {
                ["subject"] = payload.EventTitle,
                ["body"] = new JsonObject { ["contentType"] = "text", ["content"] = description },
                ["start"] = start,
                ["end"] = end
            };
    }

    private static string? CreatedId(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            return JsonNode.Parse(body) is JsonObject json && json["id"] is JsonValue id && id.TryGetValue<string>(out var text) ? NullIfEmpty(text) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static CalendarSyncCallbackRequest Callback(CalendarSyncTriggerPayload payload, bool success, string? externalEventId, string? error) =>
        new(payload.ClientId, payload.BookingId, payload.CalendarIntegrationId, payload.RequestId, success, externalEventId, error);

    // The workflow's switches had no branch for it: nothing was called and nothing called back.
    private CalendarSyncCallbackRequest? Unrouted(CalendarSyncTriggerPayload payload)
    {
        _logger.LogWarning("Calendar sync skipped: no route for provider {Provider} / operation {Operation}.", payload.Provider, payload.Operation);
        return null;
    }
}
