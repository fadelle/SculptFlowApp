using Omni.App.Business.Contracts.Jobs;
using Omni.App.Business.Contracts.HttpClients.N8n;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.Ai;

namespace Omni.App.Business.Jobs;

/// <summary>
/// Runs the AI triggers webhooks queued (IAiTriggerDispatchQueue; QA t3-inbox-22), each in its own DI scope and in its
/// message's trace, through the same IAiTriggerNotifier (BotTrigger: the plan, account and bot checks, then n8n or the code
/// engine's queue). One conversation's triggers go in the order they were queued, so two quick messages reach the bot in
/// order; different conversations go in parallel, at most <see cref="MaxConcurrent"/> at once. The notifier never throws and
/// traces its own outcome; anything else is logged.
/// </summary>
public sealed class AiTriggerDispatchWorker : BackgroundService
{
    public const int MaxConcurrent = 8;

    private readonly IAiTriggerDispatchQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AiTriggerDispatchWorker> _logger;
    private readonly SemaphoreSlim _slots = new(MaxConcurrent);

    public AiTriggerDispatchWorker(IAiTriggerDispatchQueue queue, IServiceScopeFactory scopes, ILogger<AiTriggerDispatchWorker> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tails = new Dictionary<Guid, Task>(); // each conversation's last trigger
        try
        {
            while (await _queue.WaitToReadAsync(stoppingToken))
            {
                while (_queue.TryDequeue(out var trigger))
                {
                    var previous = tails.TryGetValue(trigger.ConversationId, out var tail) ? tail : Task.CompletedTask;
                    tails[trigger.ConversationId] = RunAfterAsync(previous, trigger, stoppingToken);
                }
                if (tails.Count > 512)
                {
                    foreach (var key in tails.Where(t => t.Value.IsCompleted).Select(t => t.Key).ToList()) tails.Remove(key);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // stopping: what is still queued is dropped, like BotRunQueue
        }
    }

    private async Task RunAfterAsync(Task previous, AiTriggerPayload trigger, CancellationToken ct)
    {
        try
        {
            await previous;
        }
        catch
        {
            // the earlier trigger logged its own failure
        }
        await _slots.WaitAsync(ct);
        try
        {
            await RunAsync(trigger, ct);
        }
        finally
        {
            _slots.Release();
        }
    }

    internal async Task RunAsync(AiTriggerPayload trigger, CancellationToken ct)
    {
        using var trace = TraceContext.Continue(trigger.Traceparent, "ai.trigger");
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IAiTriggerNotifier>().NotifyAsync(trigger, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "The queued AI trigger for conversation {ConversationId} failed.", trigger.ConversationId);
        }
    }
}
