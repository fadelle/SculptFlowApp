using System.Threading.Channels;

namespace PlasticSurgery.Business.Contracts.Jobs;

/// <summary>Hands a queued benchmark run (a knowledge_retrieval_benchmark_runs id) to the background worker.</summary>
public interface IKnowledgeBenchmarkRunQueue
{
    void Enqueue(Guid runId);
    ValueTask<Guid> DequeueAsync(CancellationToken ct);
}
