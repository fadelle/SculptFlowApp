namespace PlasticSurgery.Entities.Responses.KnowledgeBenchmark;

public record BenchmarkResultListResponse(IReadOnlyList<BenchmarkResultResponse> Items, int TotalCount);
