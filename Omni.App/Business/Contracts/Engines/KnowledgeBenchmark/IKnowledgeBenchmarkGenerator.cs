using Omni.App.Entities.Dtos.KnowledgeBenchmark;

namespace Omni.App.Business.Contracts.Engines.KnowledgeBenchmark;

/// <summary>The benchmark question generator as code (Benchmark / GeneratorEngine = code): writes the questions for one
/// generation and stores them, or marks the generation failed. Never throws for a model or reply problem.</summary>
public interface IKnowledgeBenchmarkGenerator
{
    Task GenerateAsync(BenchmarkGeneratorPayload payload, CancellationToken ct = default);
}
