using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Contracts.Engines.Ai;
using Omni.App.Business.Jobs;

namespace Omni.App.Tests.Managers;

/// <summary>BotRunWorker: one run at a time per conversation in queue order, Ai / MaxConcurrentRuns across conversations,
/// each run in its message's trace, a failing run never stops the others, a stopped app refuses new runs.</summary>
public class BotRunWorkerTests
{
    private readonly BotRunQueue _queue = new();
    private readonly GatedEngine _engine = new();
    private readonly Mock<IConfigManager> _config = new();

    private BotRunWorker Worker(int maxRuns)
    {
        _config.SetupGet(c => c.AiMaxConcurrentRuns).Returns(maxRuns);
        var provider = new ServiceCollection().AddSingleton<IBotEngine>(_engine).BuildServiceProvider();
        return new BotRunWorker(_queue, provider.GetRequiredService<IServiceScopeFactory>(), _config.Object, NullLogger<BotRunWorker>.Instance);
    }

    private static AiTriggerPayload Run(Guid conversation, string text, string? traceparent = null) =>
        new(Guid.NewGuid(), conversation, Guid.NewGuid(), Guid.NewGuid(), "whatsapp", "text", text, Traceparent: traceparent, Bot: BotKit.Config);

    private static async Task Until(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("The worker didn't get there.");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task One_conversation_runs_in_order_and_others_run_beside_it_up_to_the_limit()
    {
        using var stop = new CancellationTokenSource();
        var worker = Worker(maxRuns: 2);
        await worker.StartAsync(stop.Token);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        Assert.True(_queue.TryEnqueue(Run(a, "a1")));
        _queue.TryEnqueue(Run(a, "a2"));
        _queue.TryEnqueue(Run(b, "b1"));
        _queue.TryEnqueue(Run(c, "c1"));

        await Until(() => _engine.Started.Contains("a1") && _engine.Started.Contains("b1"));
        await Task.Delay(100);
        Assert.Equal(2, _engine.Started.Count); // a2 waits for a1, c1 for a free slot

        _engine.Release("a1");
        await Until(() => _engine.Started.Count == 3);
        _engine.Release("b1");
        await Until(() => _engine.Started.Count == 4);
        _engine.Release("c1");
        _engine.Release("a2");
        await Until(() => _engine.Ended.Count == 4);

        var order = _engine.Events.ToList();
        Assert.True(order.IndexOf("end:a1") < order.IndexOf("start:a2"));
        Assert.True(_engine.MaxRunning <= 2);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_burst_is_answered_in_the_order_the_contact_wrote_it_not_the_order_it_was_saved()
    {
        // QA t7-06: "hi" lost the contact-creation race, was saved last and answered last. Telegram: same second, message_id order.
        _config.SetupGet(c => c.AiBurstWaitMilliseconds).Returns(300);
        var worker = Worker(maxRuns: 4);
        await worker.StartAsync(CancellationToken.None);
        var chat = Guid.NewGuid();
        var second = DateTimeOffset.UtcNow;
        AiTriggerPayload Msg(string text, long id) => Run(chat, text) with { SentAt = second, ProviderOrder = id };
        foreach (var text in new[] { "hi", "I want to ask", "how much", "cash?" }) _engine.Release(text);

        _queue.TryEnqueue(Msg("I want to ask", 11));
        _queue.TryEnqueue(Msg("how much", 12));
        await Task.Delay(100);
        _queue.TryEnqueue(Msg("cash?", 13));
        _queue.TryEnqueue(Msg("hi", 10)); // saved last, written first

        await Until(() => _engine.Ended.Count == 4);
        Assert.Equal(["hi", "I want to ask", "how much", "cash?"], _engine.Events.Where(e => e.StartsWith("start:")).Select(e => e[6..]));
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task One_busy_client_cant_take_every_slot()
    {
        // QA t7-05: the 4 platform-wide slots were all one client's, so another client's reply waited.
        _config.SetupGet(c => c.AiMaxConcurrentRunsPerClient).Returns(1);
        var worker = Worker(maxRuns: 4);
        await worker.StartAsync(CancellationToken.None);
        var busy = Guid.NewGuid();
        _queue.TryEnqueue(Run(Guid.NewGuid(), "busy-1") with { ClientId = busy });
        _queue.TryEnqueue(Run(Guid.NewGuid(), "busy-2") with { ClientId = busy });
        _queue.TryEnqueue(Run(Guid.NewGuid(), "other"));

        await Until(() => _engine.Started.Contains("busy-1") && _engine.Started.Contains("other"));
        await Task.Delay(100);
        Assert.DoesNotContain("busy-2", _engine.Started); // the busy client's second conversation waits for its own slot
        _engine.Release("busy-1");
        await Until(() => _engine.Started.Contains("busy-2"));
        _engine.Release("busy-2");
        _engine.Release("other");
        await Until(() => _engine.Ended.Count == 3);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Waiting_text_messages_of_a_conversation_are_answered_in_one_run()
    {
        // QA t4-ai-18: a burst of 4 messages got 4 runs, 4 replies and 4 charges.
        _config.SetupGet(c => c.AiMergeBurstMessages).Returns(true);
        _config.SetupGet(c => c.AiBurstWaitMilliseconds).Returns(300);
        var worker = Worker(maxRuns: 4);
        await worker.StartAsync(CancellationToken.None);
        var chat = Guid.NewGuid();
        var client = Guid.NewGuid();
        _engine.Release("hi\nI want to book\ntomorrow?");
        foreach (var text in new[] { "hi", "I want to book", "tomorrow?" }) _queue.TryEnqueue(Run(chat, text) with { ClientId = client });

        await Until(() => _engine.Ended.Count == 1);
        await Task.Delay(400);
        Assert.Equal(["hi\nI want to book\ntomorrow?"], _engine.Started);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void A_photo_or_voice_note_is_never_merged()
    {
        var chat = Guid.NewGuid();
        List<BotRunWorker.Pending> Queue() =>
        [
            new(Run(chat, "a"), DateTimeOffset.UtcNow, 1), new(Run(chat, "b"), DateTimeOffset.UtcNow, 2),
            new(Run(chat, "[photo]") with { MessageType = "image" }, DateTimeOffset.UtcNow, 3), new(Run(chat, "c"), DateTimeOffset.UtcNow, 4)
        ];
        var runs = Queue();
        var (first, count) = BotRunWorker.Take(runs, merge: true);
        Assert.Equal(("a\nb", 2, 2), (first.MessageText, count, runs.Count));
        var (photo, one) = BotRunWorker.Take(runs, merge: true);
        Assert.Equal(("image", 1), (photo.MessageType, one));
        var off = Queue();
        Assert.Equal(("a", 1), (BotRunWorker.Take(off, merge: false).Trigger.MessageText, 1));
        Assert.Equal(3, off.Count);
    }

    [Fact]
    public void Runs_are_ordered_by_when_they_were_written_then_the_chats_own_order_then_the_queue()
    {
        var t = DateTimeOffset.UtcNow;
        BotRunWorker.Pending P(DateTimeOffset? sent, long? order, long seq) =>
            new(Run(Guid.Empty, "x") with { SentAt = sent, ProviderOrder = order }, t, seq);

        Assert.True(BotRunWorker.Compare(P(t, 9, 1), P(t.AddSeconds(1), 1, 0)) < 0);  // earlier second first
        Assert.True(BotRunWorker.Compare(P(t, 2, 1), P(t, 1, 0)) > 0);                 // same second: message id
        Assert.True(BotRunWorker.Compare(P(null, null, 0), P(null, null, 1)) < 0);     // nothing known: queue order
    }

    [Fact]
    public async Task With_a_limit_of_one_runs_go_one_by_one_and_a_failing_run_does_not_stop_the_worker()
    {
        var worker = Worker(maxRuns: 1);
        await worker.StartAsync(CancellationToken.None);
        _engine.Fail.Add("boom");
        _queue.TryEnqueue(Run(Guid.NewGuid(), "boom"));
        _queue.TryEnqueue(Run(Guid.NewGuid(), "x"));
        _queue.TryEnqueue(Run(Guid.NewGuid(), "y"));
        _engine.Release("x");
        _engine.Release("y");

        await Until(() => _engine.Ended.Count == 2);
        Assert.Equal(1, _engine.MaxRunning);
        Assert.Equal(["start:boom", "start:x", "end:x", "start:y", "end:y"], _engine.Events);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_run_continues_its_messages_trace()
    {
        var worker = Worker(maxRuns: 4);
        await worker.StartAsync(CancellationToken.None);
        const string traceId = "4bf92f3577b34da6a3ce929d0e0e4736";
        _queue.TryEnqueue(Run(Guid.NewGuid(), "t", $"00-{traceId}-00f067aa0ba902b7-01"));
        _engine.Release("t");

        await Until(() => _engine.Ended.Count == 1);
        Assert.Equal(traceId, _engine.TraceIds["t"]);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_stopped_worker_closes_the_queue()
    {
        var worker = Worker(maxRuns: 4);
        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        Assert.False(_queue.TryEnqueue(Run(Guid.NewGuid(), "late")));
    }

    private sealed class GatedEngine : IBotEngine
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _gates = new();
        private int _running;

        public ConcurrentQueue<string> Events { get; } = new();
        public ConcurrentBag<string> Started { get; } = new();
        public ConcurrentBag<string> Ended { get; } = new();
        public ConcurrentDictionary<string, string?> TraceIds { get; } = new();
        public HashSet<string> Fail { get; } = new();
        public int MaxRunning { get; private set; }

        private TaskCompletionSource Gate(string text) => _gates.GetOrAdd(text, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        public void Release(string text) => Gate(text).TrySetResult();

        public async Task RunAsync(AiTriggerPayload trigger, CancellationToken ct = default)
        {
            var text = trigger.MessageText!;
            var running = Interlocked.Increment(ref _running);
            lock (this) MaxRunning = Math.Max(MaxRunning, running);
            Events.Enqueue("start:" + text);
            Started.Add(text);
            TraceIds[text] = TraceContext.CurrentTraceId();
            try
            {
                if (Fail.Contains(text)) throw new InvalidOperationException("engine bug");
                await Gate(text).Task.WaitAsync(ct);
                Events.Enqueue("end:" + text);
                Ended.Add(text);
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }
    }
}
