using Omni.App.Business.Contracts.Engines.Ai;
using Omni.App.Business.Contracts.Jobs;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Monitoring;
using Omni.App.Entities.Dtos.Ai;

namespace Omni.App.Business.Jobs;

/// <summary>
/// Runs the queued bot runs (IBotRunQueue) with BotEngine, each in its own DI scope and in its message's trace. One run at a
/// time per conversation (the trigger's conversationId; a test session's id for a test), in the order the contact wrote them
/// (Compare), so two quick messages are answered one after the other with the first one's memory; a conversation's next run waits
/// until its oldest waiting run has waited Ai / BurstWaitMilliseconds, so a burst is ordered first; runs of different conversations go in
/// parallel, at most Ai / MaxConcurrentRuns at once and Ai / MaxConcurrentRunsPerClient per client (QA t7-05: one busy client
/// took every slot), both read each time a run could start. With Ai / MergeBurstMessages, the plain-text runs waiting at the head
/// of a conversation start as one run with their texts in order (QA t4-ai-18: a burst got one run, reply and charge per message);
/// a photo or voice note is never merged. A run that fails is logged; the engine traces its own failures. Runs still waiting
/// when the app stops are dropped (like an n8n restart), each logged and traced as bot.run_dropped so the loss is visible.
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
        var runningClient = new Dictionary<Guid, Guid>();   // conversation key -> its client
        var perClient = new Dictionary<Guid, int>();
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
                    if (runningClient.Remove(key, out var doneClient) && --perClient[doneClient] == 0) perClient.Remove(doneClient);
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
                    var clientId = runs[0].Trigger.ClientId;
                    if (perClient.GetValueOrDefault(clientId) >= Math.Max(1, _config.AiMaxConcurrentRunsPerClient))
                    {
                        node = node.Next; // this client's slots are full; another client's conversation may start
                        continue;
                    }
                    var next = node.Next;
                    ready.Remove(node);
                    node = next;
                    var (trigger, merged) = Take(runs, _config.AiMergeBurstMessages);
                    running.Add(key);
                    runningClient[key] = clientId;
                    perClient[clientId] = perClient.GetValueOrDefault(clientId) + 1;
                    if (merged > 1) TraceMerged(trigger, merged);
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
            var dropped = waiting.Values.SelectMany(r => r).Select(p => p.Trigger).ToList();
            while (_queue.TryDequeue(out var late)) dropped.Add(late); // queued but never read
            TraceDropped(dropped);
            await Task.WhenAll(tasks.Where(t => !t.IsCompleted)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    /// <summary>The next run of a conversation: its first waiting run, or (merging on) every plain-text run at the head of the queue
    /// as one, with the texts in the order they were written and the newest message's ids and trace. Removes what it takes.</summary>
    internal static (AiTriggerPayload Trigger, int Count) Take(List<Pending> runs, bool merge)
    {
        var count = merge && IsText(runs[0].Trigger) ? runs.TakeWhile(r => IsText(r.Trigger)).Count() : 1;
        var taken = runs.Take(count).Select(r => r.Trigger).ToList();
        runs.RemoveRange(0, count);
        if (count == 1) return (taken[0], 1);
        var last = taken[^1];
        return (last with { MessageText = string.Join("\n", taken.Select(t => t.MessageText).Where(t => !string.IsNullOrWhiteSpace(t))) }, count);
    }

    private static bool IsText(AiTriggerPayload t) => t.MessageType is null or "text";

    private void TraceMerged(AiTriggerPayload trigger, int count)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            scope.ServiceProvider.GetService<IMessageTracer>()?.Step(TraceSteps.BotRunsMerged, TraceOutcomes.Ok,
                new TraceIds(trigger.ClientId, trigger.TestSessionId is null ? trigger.ConversationId : null, trigger.MessageId, trigger.Channel),
                detail: new { messages = count });
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Merged-run trace wasn't written."); }
    }

    /// <summary>Every run the app stops before it could start: logged, and traced on its message so the admin sees it.</summary>
    private void TraceDropped(IReadOnlyList<AiTriggerPayload> dropped)
    {
        if (dropped.Count == 0) return;
        _logger.LogWarning("App stopping: {Count} waiting bot run(s) dropped, conversations {Conversations}.", dropped.Count,
            string.Join(", ", dropped.Select(t => t.ConversationId).Distinct()));
        try
        {
            using var scope = _scopes.CreateScope();
            var tracer = scope.ServiceProvider.GetService<IMessageTracer>();
            foreach (var t in dropped)
            {
                tracer?.Step(TraceSteps.BotRunDropped, TraceOutcomes.Skipped,
                    new TraceIds(t.ClientId, t.TestSessionId is null ? t.ConversationId : null, t.MessageId, t.Channel), detail: new { reason = "app_stopping" });
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Dropped-run traces weren't written."); }
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
