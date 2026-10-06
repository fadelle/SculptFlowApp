using System.Text.Json;

namespace PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

/// <summary>A source chunk sent to the question generator. Only the ids and the text — never anything from another clinic.</summary>
public record BenchmarkSourceChunk(Guid DocumentId, Guid ChunkId, string DocumentTitle, string Content);
