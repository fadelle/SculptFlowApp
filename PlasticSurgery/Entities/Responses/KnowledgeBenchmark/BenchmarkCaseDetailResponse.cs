namespace PlasticSurgery.Entities.Responses.KnowledgeBenchmark;

/// <summary>A single case with the expected chunk's current full text (null when the chunk no longer exists) and its
/// most recent result, including what was retrieved.</summary>
public record BenchmarkCaseDetailResponse(
    BenchmarkCaseResponse Case,
    string? ExpectedChunkContent,
    BenchmarkResultDetailResponse? LatestResult);
