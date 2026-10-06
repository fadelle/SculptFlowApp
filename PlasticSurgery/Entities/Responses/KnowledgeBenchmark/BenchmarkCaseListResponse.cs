namespace PlasticSurgery.Entities.Responses.KnowledgeBenchmark;

public record BenchmarkCaseListResponse(IReadOnlyList<BenchmarkCaseResponse> Items, int TotalCount);
