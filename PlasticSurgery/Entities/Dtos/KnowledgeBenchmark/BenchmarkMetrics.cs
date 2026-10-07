namespace PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

/// <summary>Aggregate metrics over the SCORED cases of a run (stale and errored cases are never included). All
/// null when nothing was scored. Hit@K = share of scored cases whose expected chunk (or document) ranked &lt;= K;
/// MRR = mean of 1/rank, counting 0 when not found.</summary>
public record BenchmarkMetrics(
    int ScoredCases,
    double? ChunkTop1,
    double? ChunkTop3,
    double? ChunkTop5,
    double? ChunkMrr,
    double? DocumentTop1,
    double? DocumentTop3,
    double? DocumentTop5,
    double? DocumentMrr);
