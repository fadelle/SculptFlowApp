using Omni.App.Entities.Responses.KnowledgeBenchmark;

namespace Omni.App.Entities.Dtos.KnowledgeBenchmark;

public record BenchmarkGenerationDetail(
    BenchmarkGenerationSummary Summary,
    IReadOnlyList<RejectedGeneratedQuestion> Rejected,
    IReadOnlyList<BenchmarkGenerationChunk> SentChunks,
    /// <summary>The reply/callback body n8n sent (capped), for debugging a workflow.</summary>
    string? RawResponse);
