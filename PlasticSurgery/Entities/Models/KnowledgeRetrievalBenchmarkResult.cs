using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Models;

/// <summary>One case's outcome in one run. Question/expected-title/preview are snapshots so a run's history
/// stays readable after a case is edited or deleted.</summary>
public class KnowledgeRetrievalBenchmarkResult
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public Guid BenchmarkRunId { get; set; }
    public Guid? BenchmarkCaseId { get; set; }

    public string Question { get; set; } = string.Empty;
    public Guid ExpectedDocumentId { get; set; }
    public Guid ExpectedChunkId { get; set; }
    public string? ExpectedDocumentTitle { get; set; }
    public string? ExpectedChunkPreview { get; set; }

    /// <summary>The generation the case came from (snapshot, so per-generation scores survive deleting the case). Null for manual cases.</summary>
    public Guid? GenerationId { get; set; }

    /// <summary>1-based rank of the exact expected chunk among the chunks production search RETURNED; null = not returned.</summary>
    public int? ExpectedChunkRank { get; set; }
    /// <summary>1-based rank of the best-ranked returned chunk belonging to the expected document; null = none returned.</summary>
    public int? ExpectedDocumentBestRank { get; set; }
    public double? ExpectedChunkScore { get; set; }
    public bool ExpectedBelowThreshold { get; set; }

    public bool ChunkTop1Pass { get; set; }
    public bool ChunkTop3Pass { get; set; }
    public bool ChunkTop5Pass { get; set; }
    public bool DocumentTop1Pass { get; set; }
    public bool DocumentTop3Pass { get; set; }
    public bool DocumentTop5Pass { get; set; }

    public string ResultClassification { get; set; } = BenchmarkClassification.Miss;
    public string? StaleReason { get; set; }
    public string? ErrorMessage { get; set; }

    public int ReturnedCount { get; set; }
    /// <summary>JSON array of the ordered top-K candidates (see KnowledgeBenchmarkService.RetrievedRow).</summary>
    public string? RetrievedJson { get; set; }
    public int? LatencyMs { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
