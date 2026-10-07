namespace PlasticSurgery.Entities.Requests.KnowledgeBenchmark;

public record CreateManualBenchmarkCaseRequest(string? Question, Guid DocumentId, Guid ChunkId);
