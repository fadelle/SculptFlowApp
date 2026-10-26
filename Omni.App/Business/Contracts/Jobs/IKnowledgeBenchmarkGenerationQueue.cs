using Omni.App.Entities.Dtos.KnowledgeBenchmark;

namespace Omni.App.Business.Contracts.Jobs;

/// <summary>Hands a benchmark question generation (Benchmark / GeneratorEngine = code) to KnowledgeBenchmarkGenerationWorker.</summary>
public interface IKnowledgeBenchmarkGenerationQueue
{
    /// <summary>False only when the app is stopping.</summary>
    bool TryEnqueue(BenchmarkGeneratorPayload payload);

    ValueTask<BenchmarkGeneratorPayload> DequeueAsync(CancellationToken ct);

    void Complete();
}
