using PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

namespace PlasticSurgery.Entities.Responses.KnowledgeBenchmark;

public record BenchmarkResultDetailResponse(
    BenchmarkResultResponse Result,
    IReadOnlyList<BenchmarkRetrievedChunk> Retrieved,
    /// <summary>The expected chunk's current full text; null if it no longer exists (then only the preview is known).</summary>
    string? ExpectedChunkContent,
    /// <summary>The run's settings snapshot, so a failure can be read against the settings it ran under.</summary>
    BenchmarkSettingsSnapshot RunSettings);
