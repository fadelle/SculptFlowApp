namespace PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

/// <summary>A Knowledge Base chunk as the benchmark sees it: its document, text and (when loaded) the document title.</summary>
public record BenchmarkChunkInfo(Guid ChunkId, Guid DocumentId, string Content, string? DocumentTitle = null);
