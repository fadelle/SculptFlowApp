using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Contracts.HttpClients.N8n;
using Omni.App.Business.Jobs;
using Omni.App.Entities.Dtos.Ai;

namespace Omni.App.Tests.Managers;

/// <summary>QA t3-inbox-22: webhooks queue the AI trigger; the worker runs it afterwards, in order per conversation and in the
/// message's trace.</summary>
public class AiTriggerDispatchWorkerTests
{
    private sealed class RecordingNotifier : IAiTriggerNotifier
    {
        public readonly ConcurrentQueue<(string? Text, string? TraceId)> Seen = new();
        public readonly SemaphoreSlim Arrived = new(0);

        public async Task NotifyAsync(AiTriggerPayload payload, CancellationToken ct = default)
        {
            if (payload.MessageText == "slow") await Task.Delay(150, ct); // a slow first trigger must not be overtaken
            Seen.Enqueue((payload.MessageText, Activity.Current?.TraceId.ToHexString()));
            Arrived.Release();
        }

        public Task<bool> NotifyTestAsync(AiTriggerPayload payload, CancellationToken ct = default) => Task.FromResult(true);
    }

    private static AiTriggerPayload Trigger(Guid conversation, string text, string? traceparent = null) =>
        new(Guid.NewGuid(), conversation, Guid.NewGuid(), Guid.NewGuid(), "telegram", "text", text, null) { Traceparent = traceparent };

    [Fact]
    public async Task Queued_triggers_run_in_order_per_conversation_and_in_their_message_trace()
    {
        var notifier = new RecordingNotifier();
        var provider = new ServiceCollection().AddScoped<IAiTriggerNotifier>(_ => notifier).BuildServiceProvider();
        var queue = new AiTriggerDispatchQueue();
        var worker = new AiTriggerDispatchWorker(queue, provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AiTriggerDispatchWorker>.Instance);

        var conversation = Guid.NewGuid();
        const string traceId = "4bf92f3577b34da6a3ce929d0e0e4736";
        Assert.True(queue.TryEnqueue(Trigger(conversation, "slow", $"00-{traceId}-00f067aa0ba902b7-01")));
        Assert.True(queue.TryEnqueue(Trigger(conversation, "second")));
        Assert.True(queue.TryEnqueue(Trigger(Guid.NewGuid(), "other conversation")));

        using var stop = new CancellationTokenSource();
        await worker.StartAsync(stop.Token);
        for (var i = 0; i < 3; i++) Assert.True(await notifier.Arrived.WaitAsync(TimeSpan.FromSeconds(5)));
        await stop.CancelAsync();
        await worker.StopAsync(CancellationToken.None);

        var seen = notifier.Seen.ToList();
        var mine = seen.Where(s => s.Text != "other conversation").Select(s => s.Text).ToList();
        Assert.Equal(["slow", "second"], mine);
        Assert.Equal(traceId, seen.Single(s => s.Text == "slow").TraceId);
        // the other conversation didn't wait for the slow one
        Assert.True(seen.FindIndex(s => s.Text == "other conversation") < seen.FindIndex(s => s.Text == "slow"));
    }
}
