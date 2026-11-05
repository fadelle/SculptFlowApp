using Omni.App.Business.Contracts.Engines.Ai;
using Omni.App.Business.Contracts.Jobs;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.Ai;

namespace Omni.App.Business.Jobs;

/// <summary>
/// Runs the queued bot runs (IBotRunQueue) with BotEngine, each in its own DI scope and in its message's trace. One run at a
/// time per conversation (the trigger's conversationId; a test session's id for a test), in the order the contact wrote them
/// (Compare), so two quick messages are answered one after the other with the first one's memory; a conversation's next run waits
/// until its oldest waiting run has waited Ai / BurstWaitMilliseconds, so a burst is ordered first; runs of different conversations go in
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

    /// <summary>A queued run: when it was queued and its place in the queue (the last tie-break of the order).</summary>
    internal sealed record Pending(AiTriggerPayload Trigger, DateTimeOffset QueuedAt, long Seq);

    /// <summary>The order a conversation's waiting runs go in: the order the contact wrote them (the provider's time, then its own
    /// per-chat order, e.g. Telegram's message_id), else the order they were queued (QA t7-06: a burst's first message, saved last
    /// because it lost the contact-creation race, was answered last).</summary>
    internal static int Compare(Pending a, Pending b)
    {
        if (a.Trigger.SentAt is { } sa && b.Trigger.SentAt is { } sb && sa != sb) return sa.CompareTo(sb);
        if (a.Trigger.ProviderOrder is { } oa && b.Trigger.ProviderOrder is { } ob && oa != ob) return oa.CompareTo(ob);
        return a.Seq.CompareTo(b.Seq);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var waiting = new Dictionary<Guid, List<Pending>>();
        var ready = new LinkedList<Guid>(); // conversations with a waiting run and none running, oldest first
        var running = new HashSet<Guid>();
        var tasks = new List<Task>();
        var finished = System.Threading.Channels.Channel.CreateUnbounded<Guid>();
        long seq = 0;

        // One pending wait per source at a time (the queue has a single reader).
        Task<bool>? queued = null;
        Task<bool>? done = null;
        Task? settled = null;
        var closed = false;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                queued ??= closed ? Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => false, TaskScheduler.Default) : _queue.WaitToReadAsync(stoppingToken).AsTask();
                done ??= finished.Reader.WaitToReadAsync(stoppingToken).AsTask();
                await Task.WhenAny(settled is null ? new Task[] { queued, done } : new Task[] { queued, done, settled });
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
                if (settled?.IsCompleted == true) settled = null;

                var now = DateTimeOffset.UtcNow;
                while (_queue.TryDequeue(out var trigger))
                {
                    var key = trigger.ConversationId;
                    if (!waiting.TryGetValue(key, out var runs)) waiting[key] = runs = new List<Pending>();
                    runs.Add(new Pending(trigger, now, seq++));
                    runs.Sort(Compare);
                    if (!running.Contains(key) && !ready.Contains(key)) ready.AddLast(key);
                }
                while (finished.Reader.TryRead(out var key))
                {
                    running.Remove(key);
                    if (waiting.TryGetValue(key, out var runs) && runs.Count > 0) ready.AddLast(key);
                    else waiting.Remove(key);
                }
                tasks.RemoveAll(t => t.IsCompleted);

                // A conversation's next run starts once its oldest waiting run has waited Ai / BurstWaitMilliseconds, so the rest
                // of a burst (delivered at the same moment, saved a little later) is queued and ordered first (QA t7-06).
                var wait = TimeSpan.FromMilliseconds(Math.Max(0, _config.AiBurstWaitMilliseconds));
                DateTimeOffset? nextDue = null;
                for (var node = ready.First; node is not null && running.Count < Math.Max(1, _config.AiMaxConcurrentRuns);)
                {
                    var key = node.Value;
                    var runs = waiting[key];
                    var due = runs.Min(r => r.QueuedAt) + wait;
                    if (due > now)
                    {
                        nextDue = nextDue is null || due < nextDue ? due : nextDue;
                        node = node.Next;
                        continue;
                    }
                    var next = node.Next;
                    ready.Remove(node);
                    node = next;
                    var trigger = runs[0].Trigger;
                    runs.RemoveAt(0);
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
                // Wake up when the next waiting conversation is due (a timer already set is never later: new runs are due later).
                if (nextDue is { } at && settled is null)
                {
                    var delay = at - DateTimeOffset.UtcNow;
                    settled = Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, stoppingToken);
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
