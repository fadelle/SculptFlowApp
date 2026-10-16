using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Monitoring;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Monitoring;

namespace Omni.App.Business.Jobs;

/// <summary>
/// Every AppLogLimits.FlushSeconds: copies the ErrorLog settings into IAppLogCollector, writes the waiting entries to
/// activity.app_logs in batches, and every AppLogLimits.PurgeIntervalMinutes deletes rows past retention. Flushes once
/// more on shutdown. Everything runs inside IAppLogCollector.Suppress, so a failing write can't log itself into the
/// buffer again; a failed batch is put back and retried next time (the buffer's cap still bounds memory).
/// </summary>
public sealed class AppLogFlushJob : BackgroundService
{
    private readonly IAppLogCollector _collector;
    private readonly IConfigManager _config;
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _time;
    private readonly ILogger<AppLogFlushJob> _logger;
    private DateTimeOffset _lastPurge = DateTimeOffset.MinValue;

    public AppLogFlushJob(IAppLogCollector collector, IConfigManager config, IServiceScopeFactory scopes, TimeProvider time,
        ILogger<AppLogFlushJob> logger)
    {
        _collector = collector;
        _config = config;
        _scopes = scopes;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(AppLogLimits.FlushSeconds), _time);
            do
            {
                await FlushAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down; StopAsync writes what is left
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await FlushAsync(cancellationToken);
    }

    /// <summary>Applies the settings, writes everything waiting and, when due, purges. Never throws (except on cancel).</summary>
    public async Task FlushAsync(CancellationToken ct)
    {
        using var _ = _collector.Suppress();
        ApplySettings();

        var now = _time.GetUtcNow();
        var batch = _collector.Drain(now, AppLogLimits.BatchSize);
        var purgeDue = now - _lastPurge >= TimeSpan.FromMinutes(AppLogLimits.PurgeIntervalMinutes);
        if (batch.Count == 0 && !purgeDue) return;

        try
        {
            using var scope = _scopes.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IAppLogService>();
            // Bounded, so a flood can't keep this flush running forever; the rest waits for the next tick.
            for (var round = 0; batch.Count > 0 && round < AppLogLimits.BufferCapacity / AppLogLimits.BatchSize; round++)
            {
                try
                {
                    await service.SaveAsync(batch, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _collector.Requeue(batch);
                    _logger.LogWarning(ex, "Error log: writing {Rows} row(s) failed; they will be retried.", batch.Count);
                    return;
                }
                batch = batch.Count < AppLogLimits.BatchSize ? [] : _collector.Drain(now, AppLogLimits.BatchSize);
            }
            if (batch.Count > 0) _collector.Requeue(batch);

            if (purgeDue)
            {
                _lastPurge = now;
                var deleted = await service.PurgeAsync(ct);
                if (deleted > 0) _logger.LogInformation("Error log: deleted {Rows} row(s) past retention.", deleted);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Error log: flush failed.");
        }
    }

    private void ApplySettings() =>
        _collector.Apply(AppLogCaptureSettings.From(
            _config.ErrorLogEnabled,
            _config.ErrorLogMinimumLevel,
            _config.ErrorLogExcludedCategories,
            _config.ErrorLogMaxPerFingerprintPerMinute));
}
