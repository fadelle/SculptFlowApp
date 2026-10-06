namespace PlasticSurgery.Entities.Responses.KnowledgeBenchmark;

public record BenchmarkSourceDocumentResponse(Guid Id, string Title, string Category, int ChunkCount);
