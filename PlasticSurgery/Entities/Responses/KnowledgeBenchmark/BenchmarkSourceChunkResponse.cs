namespace PlasticSurgery.Entities.Responses.KnowledgeBenchmark;

public record BenchmarkSourceChunkResponse(Guid Id, int ChunkIndex, string Preview, string Content);
