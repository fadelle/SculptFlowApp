namespace Omni.App.Entities.Responses.KnowledgeBenchmark;

public record BenchmarkResultListResponse(IReadOnlyList<BenchmarkResultResponse> Items, int TotalCount);
