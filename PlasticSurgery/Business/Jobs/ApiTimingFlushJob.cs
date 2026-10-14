using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Contracts.Services.Monitoring;
using PlasticSurgery.Common.Statics;

namespace PlasticSurgery.Business.Jobs;

/// <summary>
/// On every whole minute, drains the in-memory request timings (IApiTimingCollector) into one activity.api_route_stats
/// row per method + route, and every ApiTimingLimits.PurgeIntervalMinutes deletes rows older than
/// Monitoring:RetentionDays. Flushes once more on shutdown. Timings are best effort: if a write fails, that minute's
/// rows are dropped and logged, never retried (they must not pile up in memory).
/// </summary>
public sealed class ApiTimingFlushJob : BackgroundService
{
    private readonly IApiTimingCollector _collector;
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _time;
    private readonly ILogger<ApiTimingFlushJob> _logger;
    private DateTimeOffset _lastPurge = DateTimeOffset.MinValue;

    public ApiTimingFlushJob(IApiTimingCollector collector, IServiceScopeFactory scopes, TimeProvider time, ILogger<ApiTimingFlushJob> logger)
    {
        _collector = collector;
        _scopes = scopes;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Line the windows up with whole minutes, so each row is one clock minute.
            var now = _time.GetUtcNow();
            var untilNextMinute = TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond);
            await Task.Delay(untilNextMinute, _time, stoppingToken);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(ApiTimingLimits.FlushSeconds), _time);
            do
            {
                await FlushAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down; StopAsync writes the last partial minute
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await FlushAsync(cancellationToken);
    }

    /// <summary>Writes the window that just ended and, when due, deletes expired rows. Never throws (except on cancel).</summary>
    public async Task FlushAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var rows = _collector.Drain(now);
        var purgeDue = now - _lastPurge >= TimeSpan.FromMinutes(ApiTimingLimits.PurgeIntervalMinutes);
        if (rows.Count == 0 && !purgeDue) return;

        try
        {
            using var scope = _scopes.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IApiTimingService>();
            await service.SaveAsync(rows, ct);
            if (purgeDue)
            {
                _lastPurge = now;
                var deleted = await service.PurgeAsync(ct);
                if (deleted > 0) _logger.LogInformation("API timings: deleted {Rows} row(s) past retention.", deleted);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "API timings: flush failed; {Rows} route summary row(s) for this minute were dropped.", rows.Count);
        }
    }
}
