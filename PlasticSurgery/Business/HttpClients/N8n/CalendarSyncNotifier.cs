using System.Text.Json;
using PlasticSurgery.Business.Contracts.HttpClients.N8n;
using PlasticSurgery.Entities.Requests.Calendars;

namespace PlasticSurgery.Business.HttpClients.N8n;

public class CalendarSyncNotifier : ICalendarSyncNotifier
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CalendarSyncNotifier> _logger;

    public CalendarSyncNotifier(HttpClient http, IConfiguration configuration, ILogger<CalendarSyncNotifier> logger)
    {
        _http = http;
        _configuration = configuration;
        _logger = logger;
    }

    public Task NotifySyncAsync(CalendarSyncTriggerPayload payload, CancellationToken ct = default) =>
        PostAsync("N8n:CalendarSyncWebhookUrl", payload, payload.CalendarIntegrationId, ct);

    /// <summary>Never throws — logs and swallows instead. A missed sync is a separate integration issue (see
    /// CalendarIntegrationService's health tracking), never a reason to fail or roll back the caller.</summary>
    private async Task PostAsync<T>(string configKey, T payload, Guid calendarIntegrationId, CancellationToken ct)
    {
        var url = _configuration[configKey];
        if (string.IsNullOrWhiteSpace(url))
        {
            _logger.LogWarning("{ConfigKey} is not configured — skipping calendar sync call for integration {CalendarIntegrationId}.",
                configKey, calendarIntegrationId);
            return;
        }

        try
        {
            using var response = await _http.PostAsJsonAsync(url, payload, JsonOptions, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("n8n calendar webhook ({ConfigKey}) returned {StatusCode} for integration {CalendarIntegrationId}: {Body}",
                    configKey, (int)response.StatusCode, calendarIntegrationId, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to call n8n calendar webhook ({ConfigKey}) for integration {CalendarIntegrationId}.",
                configKey, calendarIntegrationId);
        }
    }
}
