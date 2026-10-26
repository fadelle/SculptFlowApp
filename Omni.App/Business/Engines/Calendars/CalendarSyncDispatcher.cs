using Omni.App.Business.Contracts.HttpClients.N8n;
using Omni.App.Business.Contracts.Jobs;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.HttpClients.N8n;
using Omni.App.Common.Statics;
using Omni.App.Entities.Requests.Calendars;

namespace Omni.App.Business.Engines.Calendars;

/// <summary>
/// The calendar sync trigger (ICalendarSyncNotifier, called by CalendarIntegrationService after a booking change is saved).
/// Calendars / SyncEngine, read for every sync, picks the engine: n8n → CalendarSyncNotifier posts to
/// N8n:CalendarSyncWebhookUrl unchanged; code → the sync is queued for CalendarSyncWorker (CalendarSyncClient, then
/// ApplySyncCallbackAsync). Either way it returns at once and never throws.
/// </summary>
public class CalendarSyncDispatcher : ICalendarSyncNotifier
{
    private readonly IConfigManager _config;
    private readonly CalendarSyncNotifier _n8n;
    private readonly ICalendarSyncQueue _queue;
    private readonly ILogger<CalendarSyncDispatcher> _logger;

    public CalendarSyncDispatcher(IConfigManager config, CalendarSyncNotifier n8n, ICalendarSyncQueue queue, ILogger<CalendarSyncDispatcher> logger)
    {
        _config = config;
        _n8n = n8n;
        _queue = queue;
        _logger = logger;
    }

    public async Task NotifySyncAsync(CalendarSyncTriggerPayload payload, CancellationToken ct = default)
    {
        if (!string.Equals(_config.CalendarsSyncEngine, WorkflowEngines.Code, StringComparison.OrdinalIgnoreCase))
        {
            await _n8n.NotifySyncAsync(payload, ct);
            return;
        }

        if (!_queue.TryEnqueue(payload))
        {
            _logger.LogWarning("Calendar sync not queued for integration {IntegrationId}: the app is stopping.", payload.CalendarIntegrationId);
        }
    }
}
