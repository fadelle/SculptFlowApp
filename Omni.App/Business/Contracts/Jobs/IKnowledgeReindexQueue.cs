using Omni.App.Entities.Dtos.Knowledge;

namespace Omni.App.Business.Contracts.Jobs;

/// <summary>
/// Re-indexes a client's whole Knowledge Base in the background (QA t4-ai-15: a changed chunk size applied only to entries
/// re-saved by hand). Started by Knowledge Base → Settings (by itself when the chunk size or overlap changes, or with
/// "Re-index now") and by the admin portal; KnowledgeReindexWorker does the work.
/// </summary>
public interface IKnowledgeReindexQueue
{
    /// <summary>Queues the client's re-index; false when one is already queued or running (nothing new is started).</summary>
    bool Start(Guid clientId, DateTimeOffset now);

    /// <summary>The client's current or last re-index since the app started, or null.</summary>
    KnowledgeReindexProgress? Progress(Guid clientId);

    // For the worker.
    ValueTask<bool> WaitToReadAsync(CancellationToken ct);
    bool TryDequeue(out Guid clientId);
    void Report(Guid clientId, KnowledgeReindexProgress progress);
}
