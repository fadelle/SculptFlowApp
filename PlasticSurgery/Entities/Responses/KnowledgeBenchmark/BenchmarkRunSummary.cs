using PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

namespace PlasticSurgery.Entities.Responses.KnowledgeBenchmark;

public record BenchmarkRunSummary(
    Guid Id,
    string CaseScope,
    /// <summary>Set only when CaseScope is "generation".</summary>
    Guid? GenerationId,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    int TotalCases,
    int ProcessedCases,
    int ScoredCases,
    int StaleCases,
    int ErrorCases,
    double? ChunkTop1Accuracy,
    double? ChunkTop3Accuracy,
    double? ChunkTop5Accuracy,
    double? ChunkMrr,
    double? DocumentTop1Accuracy,
    double? DocumentTop3Accuracy,
    double? DocumentTop5Accuracy,
    double? DocumentMrr,
    double? AverageLatencyMs,
    BenchmarkSettingsSnapshot Settings,
    string? ErrorSummary);
