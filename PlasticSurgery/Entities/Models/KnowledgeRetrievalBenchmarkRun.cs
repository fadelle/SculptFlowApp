using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Models;

/// <summary>One benchmark execution: which cases it covered, the strict-chunk and secondary document-level
/// metrics it produced, and a snapshot of the retrieval settings it ran with (the clinic's Knowledge Search
/// Settings remain the source of truth — this only records what was used).</summary>
public class KnowledgeRetrievalBenchmarkRun
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }

    public string CaseScope { get; set; } = BenchmarkCaseScope.All;
    /// <summary>Set only when CaseScope is "generation" — which generation's cases this run scored.</summary>
    public Guid? GenerationId { get; set; }
    public string Status { get; set; } = BenchmarkRunStatus.Pending;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public int TotalCases { get; set; }
    public int ProcessedCases { get; set; }
    public int ScoredCases { get; set; }
    public int StaleCases { get; set; }
    public int ErrorCases { get; set; }

    public double? ChunkTop1Accuracy { get; set; }
    public double? ChunkTop3Accuracy { get; set; }
    public double? ChunkTop5Accuracy { get; set; }
    public double? DocumentTop1Accuracy { get; set; }
    public double? DocumentTop3Accuracy { get; set; }
    public double? DocumentTop5Accuracy { get; set; }
    public double? ChunkMrr { get; set; }
    public double? DocumentMrr { get; set; }
    public double? AverageLatencyMs { get; set; }

    public string? EmbeddingModel { get; set; }
    public int? VectorDimension { get; set; }
    public int? ChunkSizeTokens { get; set; }
    public int? ChunkOverlapTokens { get; set; }
    public string? SimilarityMethod { get; set; }
    public int? TopK { get; set; }
    public double? MinimumSimilarity { get; set; }
    public int? IndexedChunkCount { get; set; }
    public double? AvgChunkChars { get; set; }

    public string? ErrorSummary { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
