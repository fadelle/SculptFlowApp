using Omni.App.Entities.Dtos.Ai;

namespace Omni.App.Business.Contracts.Jobs;

/// <summary>Hands a bot run (a complete AI trigger: message, bot, client profile) to BotRunWorker. Enqueueing never waits;
/// the webhook or test panel that triggered the run answers at once.</summary>
public interface IBotRunQueue
{
    /// <summary>False only when the queue no longer accepts runs (the app is stopping).</summary>
    bool TryEnqueue(AiTriggerPayload trigger);

    /// <summary>Waits until a run is waiting (false once the queue is closed and empty).</summary>
    ValueTask<bool> WaitToReadAsync(CancellationToken ct);

    bool TryDequeue(out AiTriggerPayload trigger);

    /// <summary>Stops accepting runs (app shutdown).</summary>
    void Complete();
}
