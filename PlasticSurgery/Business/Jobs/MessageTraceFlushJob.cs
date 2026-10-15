using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Contracts.Services.Monitoring;
using PlasticSurgery.Common.Statics;

namespace PlasticSurgery.Business.Jobs;

/// <summary>
/// Every MessageTraceLimits.FlushSeconds: writes the steps IMessageTracer recorded to activity.message_trace_steps in
/// batches, and every MessageTraceLimits.PurgeIntervalMinutes deletes steps past retention. Flushes once more on
/// shutdown. A failed batch is put back and retried (the tracer's buffer cap bounds memory); dropped steps are logged.
/// </summary>
public sealed class MessageTraceFlushJob : BackgroundService
{
    private readonly IMessageTracer _tracer;
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _time;
    private readonly ILogger<MessageTraceFlushJob> _logger;
    private DateTimeOffset _lastPurge = DateTimeOffset.MinValue;

    public MessageTraceFlushJob(IMessageTracer tracer, IServiceScopeFactory scopes, TimeProvider time, ILogger<MessageTraceFlushJob> logger)
    {
        _tracer = tracer;
        _scopes = scopes;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(MessageTraceLimits.FlushSeconds), _time);
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

    /// <summary>Writes everything waiting and, when due, purges. Never throws (except on cancel).</summary>
    public async Task FlushAsync(CancellationToken ct)
    {
        var dropped = _tracer.TakeDropped();
        if (dropped > 0) _logger.LogWarning("Message trace: {Steps} step(s) were dropped because the buffer was full.", dropped);

        var now = _time.GetUtcNow();
        var batch = _tracer.Drain(MessageTraceLimits.BatchSize);
        var purgeDue = now - _lastPurge >= TimeSpan.FromMinutes(MessageTraceLimits.PurgeIntervalMinutes);
        if (batch.Count == 0 && !purgeDue) return;

        try
        {
            using var scope = _scopes.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IMessageTraceService>();
            for (var round = 0; batch.Count > 0 && round < MessageTraceLimits.BufferCapacity / MessageTraceLimits.BatchSize; round++)
            {
                try
                {
                    await service.SaveAsync(batch, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _tracer.Requeue(batch);
                    _logger.LogWarning(ex, "Message trace: writing {Rows} step(s) failed; they will be retried.", batch.Count);
                    return;
                }
                batch = batch.Count < MessageTraceLimits.BatchSize ? [] : _tracer.Drain(MessageTraceLimits.BatchSize);
            }
            if (batch.Count > 0) _tracer.Requeue(batch);

            if (purgeDue)
            {
                _lastPurge = now;
                var deleted = await service.PurgeAsync(ct);
                if (deleted > 0) _logger.LogInformation("Message trace: deleted {Rows} step(s) past retention.", deleted);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Message trace: flush failed.");
        }
    }
}