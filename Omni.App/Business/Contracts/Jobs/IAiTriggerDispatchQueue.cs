using Omni.App.Entities.Dtos.Ai;

namespace Omni.App.Business.Contracts.Jobs;

/// <summary>
/// Hands an AI trigger from a webhook to AiTriggerDispatchWorker, so the webhook answers once the message is saved and the
/// trigger's checks and its n8n call run afterwards (QA t3-inbox-22: a Telegram webhook took ~1.8 s). The trigger carries
/// its message's traceparent, so its steps stay in the message's trace.
/// </summary>
public interface IAiTriggerDispatchQueue
{
    /// <summary>False when the queue is full or closed: the caller then triggers inline, as before.</summary>
    bool TryEnqueue(AiTriggerPayload trigger);

    ValueTask<bool> WaitToReadAsync(CancellationToken ct);

    bool TryDequeue(out AiTriggerPayload trigger);
}
