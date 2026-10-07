namespace PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

/// <summary>Strict-chunk and document-level metrics for ONE generation's cases in ONE run (see KnowledgeBenchmarkService).</summary>
public record BenchmarkGenerationScores(
    Guid? RunId,
    DateTimeOffset? RunAt,
    int ScoredCases,
    double? ChunkTop1,
    double? ChunkTop3,
    double? ChunkTop5,
    double? ChunkMrr,
    double? DocumentTop1,
    double? DocumentTop3,
    double? DocumentTop5,
    double? DocumentMrr);
