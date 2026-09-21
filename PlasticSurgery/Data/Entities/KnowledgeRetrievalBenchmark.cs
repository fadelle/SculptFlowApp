namespace PlasticSurgery.Data.Entities;

// Knowledge Retrieval Benchmark (see Integrations/Knowledge/Benchmark). These entities are a standalone
// diagnostic feature: nothing in production ingestion or search reads or writes them, and no benchmark state
// is stored on KnowledgeDocument / KnowledgeChunk.

/// <summary>One realistic patient question plus the single chunk that is known to answer it. The expected
/// document/chunk ids are plain columns (not foreign keys) on purpose: chunks get new ids whenever their
/// document is re-saved, and the benchmark must never block production ingestion. A case whose chunk has
/// disappeared or changed is flagged <see cref="IsStale"/> and excluded from scoring.</summary>
public class KnowledgeRetrievalBenchmarkCase
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }

    public string Question { get; set; } = string.Empty;

    public Guid ExpectedDocumentId { get; set; }
    public Guid ExpectedChunkId { get; set; }

    /// <summary>"generated" (question written by the n8n generator) or "manual" (added by hand).</summary>
    public string CaseType { get; set; } = BenchmarkCaseType.Generated;
    public bool IsReviewed { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }

    /// <summary>The "Generate Test Cases" request that produced this case — sent to the n8n generator and required back in
    /// its response. Null for manual cases (and for cases created before generation ids existed).</summary>
    public Guid? GenerationId { get; set; }

    /// <summary>SHA-256 (hex) of the expected chunk's content when the case was created/linked.</summary>
    public string? SourceChunkHash { get; set; }
    public string? SourceChunkPreview { get; set; }
    public string? SourceDocumentTitle { get; set; }

    public bool IsStale { get; set; }
    public string? StaleReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One benchmark execution: which cases it covered, the strict-chunk and secondary document-level
/// metrics it produced, and a snapshot of the retrieval settings it ran with (the clinic's Knowledge Search
/// Settings remain the source of truth — this only records what was used).</summary>
public class KnowledgeRetrievalBenchmarkRun
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }

    public string CaseScope { get; set; } = BenchmarkCaseScope.All;
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

public static class BenchmarkCaseType
{
    public const string Generated = "generated";
    public const string Manual = "manual";
}

/// <summary>Which cases a run covers. "reviewed" = every case a human has marked reviewed (generated or manual).</summary>
public static class BenchmarkCaseScope
{
    public const string All = "all";
    public const string Generated = "generated";
    public const string Reviewed = "reviewed";

    public static bool IsValid(string? value) => value is All or Generated or Reviewed;
}

public static class BenchmarkRunStatus
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public static class BenchmarkClassification
{
    public const string ExactChunkHit = "EXACT_CHUNK_HIT";
    public const string DocumentOnlyHit = "DOCUMENT_ONLY_HIT";
    public const string Miss = "MISS";
    /// <summary>The expected chunk no longer exists / changed / its document is inactive — not scored.</summary>
    public const string StaleCase = "STALE_CASE";
    /// <summary>The retrieval call itself failed (e.g. the embedding provider) — not scored, not a retrieval miss.</summary>
    public const string Error = "ERROR";
}

public static class BenchmarkStaleReason
{
    public const string ChunkMissing = "chunk_missing";
    public const string ChunkChanged = "chunk_changed";
    public const string DocumentInactive = "document_inactive";

    public static string Label(string? reason) => reason switch
    {
        ChunkMissing => "The chunk no longer exists (the document was re-saved, re-chunked or deleted).",
        ChunkChanged => "The chunk's text changed since this case was created.",
        DocumentInactive => "The chunk's document is inactive, so search never returns it.",
        _ => string.Empty
    };
}
