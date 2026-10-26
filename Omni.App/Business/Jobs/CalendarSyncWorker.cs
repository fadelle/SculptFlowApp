using System.Threading.Channels;
using Omni.App.Business.Contracts.HttpClients.Calendars;
using Omni.App.Business.Contracts.Jobs;
using Omni.App.Business.Contracts.Services.Calendars;
using Omni.App.Entities.Requests.Calendars;

namespace Omni.App.Business.Jobs;

/// <summary>
/// Runs queued calendar syncs (Calendars / SyncEngine = code) one at a time, in the order the booking changes happened, each
/// in its own DI scope: CalendarSyncClient makes the provider call, then ICalendarIntegrationService.ApplySyncCallbackAsync
/// records it exactly as n8n's callback did. A sync still queued when the app stops is lost and stays pending, like an n8n
/// run that never called back.
/// </summary>
public sealed class CalendarSyncWorker : BackgroundService
{
    private readonly ICalendarSyncQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<CalendarSyncWorker> _logger;

    public CalendarSyncWorker(ICalendarSyncQueue queue, IServiceScopeFactory scopes, ILogger<CalendarSyncWorker> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            CalendarSyncTriggerPayload payload;
            try
            {
                payload = await _queue.DequeueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ChannelClosedException)
            {
                break;
            }

            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<ICalendarSyncClient>().SyncAsync(payload, stoppingToken);
                if (result is not null)
                {
                    await scope.ServiceProvider.GetRequiredService<ICalendarIntegrationService>().ApplySyncCallbackAsync(result, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Calendar sync failed for booking {BookingId} on integration {IntegrationId}.", payload.BookingId, payload.CalendarIntegrationId);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.Complete();
        await base.StopAsync(cancellationToken);
    }
}
