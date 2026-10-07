using PlasticSurgery.Entities.Responses.KnowledgeBenchmark;

namespace PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

public record BenchmarkGenerationDetail(
    BenchmarkGenerationSummary Summary,
    IReadOnlyList<RejectedGeneratedQuestion> Rejected,
    IReadOnlyList<BenchmarkGenerationChunk> SentChunks,
    /// <summary>The reply/callback body n8n sent (capped), for debugging a workflow.</summary>
    string? RawResponse);
