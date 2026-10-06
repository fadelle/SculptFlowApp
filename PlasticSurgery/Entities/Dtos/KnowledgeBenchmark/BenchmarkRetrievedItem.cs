namespace PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

/// <summary>One chunk production search RETURNED for a question, in rank order (index 0 = rank 1). Chunks that
/// scored under the clinic's minimum similarity are never passed here — production search would not return them.</summary>
public record BenchmarkRetrievedItem(Guid DocumentId, Guid ChunkId);
