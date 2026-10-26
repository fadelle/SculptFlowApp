using Omni.App.Business.Contracts.Engines.Ai;
using Omni.App.Business.Contracts.Jobs;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.Ai;

namespace Omni.App.Business.Jobs;

/// <summary>
/// Runs the queued bot runs (IBotRunQueue) with BotEngine, each in its own DI scope and in its message's trace. One run at a
/// time per conversation (the trigger's conversationId; a test session's id for a test), in the order they were queued, so
/// two quick messages are answered one after the other with the first one's memory; runs of different conversations go in
/// parallel, at most Ai / MaxConcurrentRuns at once (read each time a run could start). A run that fails is logged; the
/// engine traces its own failures. Runs still waiting when the app stops are dropped (like an n8n restart).
/// </summary>
public sealed class BotRunWorker : BackgroundService
{
    private readonly IBotRunQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfigManager _config;
    private readonly ILogger<BotRunWorker> _logger;

    public BotRunWorker(IBotRunQueue queue, IServiceScopeFactory scopes, IConfigManager config, ILogger<BotRunWorker> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var waiting = new Dictionary<Guid, Queue<AiTriggerPayload>>();
        var ready = new LinkedList<Guid>(); // conversations with a waiting run and none running, oldest first
        var running = new HashSet<Guid>();
        var tasks = new List<Task>();
        var finished = System.Threading.Channels.Channel.CreateUnbounded<Guid>();

        // One pending wait per source at a time (the queue has a single reader).
        Task<bool>? queued = null;
        Task<bool>? done = null;
        var closed = false;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                queued ??= closed ? Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => false, TaskScheduler.Default) : _queue.WaitToReadAsync(stoppingToken).AsTask();
                done ??= finished.Reader.WaitToReadAsync(stoppingToken).AsTask();
                await Task.WhenAny(queued, done);
                stoppingToken.ThrowIfCancellationRequested();
                if (queued.IsCompleted)
                {
                    closed |= !await queued;
                    queued = null;
                }
                if (done.IsCompleted)
                {
                    await done;
                    done = null;
                }

                while (_queue.TryDequeue(out var trigger))
                {
                    var key = trigger.ConversationId;
                    if (!waiting.TryGetValue(key, out var runs)) waiting[key] = runs = new Queue<AiTriggerPayload>();
                    runs.Enqueue(trigger);
                    if (!running.Contains(key) && !ready.Contains(key)) ready.AddLast(key);
                }
                while (finished.Reader.TryRead(out var key))
                {
                    running.Remove(key);
                    if (waiting.TryGetValue(key, out var runs) && runs.Count > 0) ready.AddLast(key);
                    else waiting.Remove(key);
                }
                tasks.RemoveAll(t => t.IsCompleted);

                while (ready.First is { } next && running.Count < Math.Max(1, _config.AiMaxConcurrentRuns))
                {
                    ready.RemoveFirst();
                    var key = next.Value;
                    var trigger = waiting[key].Dequeue();
                    running.Add(key);
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await RunAsync(trigger, stoppingToken);
                        }
                        finally
                        {
                            finished.Writer.TryWrite(key);
                        }
                    }, CancellationToken.None));
                }

                if (closed && running.Count == 0 && ready.Count == 0) break; // queue closed and every run done
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _queue.Complete();
            await Task.WhenAll(tasks.Where(t => !t.IsCompleted)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    // Also here: a worker stopped before ExecuteAsync ever ran must still refuse new runs.
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.Complete();
        await base.StopAsync(cancellationToken);
    }

    private async Task RunAsync(AiTriggerPayload trigger, CancellationToken ct)
    {
        try
        {
            using var trace = TraceContext.Continue(trigger.Traceparent, "bot.run");
            await using var scope = _scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IBotEngine>().RunAsync(trigger, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // BotEngine traces its own failures; this only guards the worker.
            _logger.LogError(ex, "Bot run failed for conversation {ConversationId}.", trigger.ConversationId);
        }
    }
}
