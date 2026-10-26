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
