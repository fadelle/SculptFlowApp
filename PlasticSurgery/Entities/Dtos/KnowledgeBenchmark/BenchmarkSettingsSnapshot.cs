namespace PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

// Knowledge Retrieval Benchmark — see Integrations/Knowledge/Benchmark. No request here carries a clinicId:
// the clinic always comes from CurrentClinicContext.

/// <summary>The settings a run used (a snapshot — Knowledge Search Settings stay the source of truth) plus what the
/// index actually contained at the time.</summary>
public record BenchmarkSettingsSnapshot(
    string? EmbeddingModel,
    int? VectorDimension,
    int? ChunkSizeTokens,
    int? ChunkOverlapTokens,
    string? SimilarityMethod,
    int? TopK,
    double? MinimumSimilarity,
    int? IndexedChunkCount,
    double? AvgChunkChars);
