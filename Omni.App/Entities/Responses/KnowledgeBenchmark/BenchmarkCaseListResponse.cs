namespace Omni.App.Entities.Responses.KnowledgeBenchmark;

public record BenchmarkCaseListResponse(IReadOnlyList<BenchmarkCaseResponse> Items, int TotalCount);
