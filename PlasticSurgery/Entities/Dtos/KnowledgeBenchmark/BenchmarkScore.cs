namespace PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

/// <summary>The deterministic outcome of scoring one case against the ranked results. Ranks are 1-based and null when
/// not found; the *TopN flags are "found at rank &lt;= N".</summary>
public record BenchmarkScore(
    int? ExpectedChunkRank,
    int? ExpectedDocumentBestRank,
    bool ChunkTop1,
    bool ChunkTop3,
    bool ChunkTop5,
    bool DocumentTop1,
    bool DocumentTop3,
    bool DocumentTop5,
    double ChunkReciprocalRank,
    double DocumentReciprocalRank,
    string Classification);
