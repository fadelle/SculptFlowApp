namespace PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

/// <summary>One chunk production search returned (or, when <see cref="PassedThreshold"/> is false, one that made the
/// top-K but scored under the clinic's minimum similarity so production would have dropped it).</summary>
public record BenchmarkRetrievedChunk(
    int Rank,
    Guid DocumentId,
    Guid ChunkId,
    string Title,
    double Score,
    bool PassedThreshold,
    bool IsExpectedChunk,
    bool IsExpectedDocument,
    string Preview);
