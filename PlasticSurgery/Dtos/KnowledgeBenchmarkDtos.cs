namespace PlasticSurgery.Dtos;

// Knowledge Retrieval Benchmark — see Integrations/Knowledge/Benchmark. No request here carries a clinicId:
// the clinic always comes from CurrentClinicContext.

/// <summary>The settings a run used (a snapshot — Knowledge Search Settings stay the source of truth) plus what the
/// index actually contained at the time.</summary>
public record BenchmarkSettingsSnapshot(
    string? EmbeddingModel,
    int? VectorDimension,
    int? ChunkSizeTokens,
    int? ChunkOverlapTokens,
    string? SimilarityMethod,
    int? TopK,
    double? MinimumSimilarity,
    int? IndexedChunkCount,
    double? AvgChunkChars);

public record BenchmarkRunSummary(
    Guid Id,
    string CaseScope,
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

public record BenchmarkDashboardResponse(
    int TotalCases,
    int GeneratedCases,
    int ManualCases,
    int ReviewedCases,
    int StaleCases,
    bool GeneratorConfigured,
    bool RunInProgress,
    /// <summary>The clinic's CURRENT retrieval settings (what a run started now would use).</summary>
    BenchmarkSettingsSnapshot CurrentSettings,
    /// <summary>The most recent run of any status (drives the progress bar).</summary>
    BenchmarkRunSummary? LatestRun,
    /// <summary>The most recent COMPLETED run — the headline metrics come from this one.</summary>
    BenchmarkRunSummary? LatestCompletedRun);

public record BenchmarkCaseLastResult(
    Guid ResultId,
    Guid RunId,
    string Classification,
    int? ExpectedChunkRank,
    int? ExpectedDocumentBestRank,
    DateTimeOffset RanAt);

public record BenchmarkCaseResponse(
    Guid Id,
    string Question,
    string CaseType,
    bool IsReviewed,
    DateTimeOffset? ReviewedAt,
    /// <summary>The generation request that created this case (null for manual cases).</summary>
    Guid? GenerationId,
    bool IsStale,
    string? StaleReason,
    string? StaleExplanation,
    Guid ExpectedDocumentId,
    Guid ExpectedChunkId,
    string? ExpectedDocumentTitle,
    string? ExpectedChunkPreview,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    BenchmarkCaseLastResult? LastResult);

public record BenchmarkCaseListResponse(IReadOnlyList<BenchmarkCaseResponse> Items, int TotalCount);

/// <summary>A single case with the expected chunk's current full text (null when the chunk no longer exists) and its
/// most recent result, including what was retrieved.</summary>
public record BenchmarkCaseDetailResponse(
    BenchmarkCaseResponse Case,
    string? ExpectedChunkContent,
    BenchmarkResultDetailResponse? LatestResult);

public record CreateManualBenchmarkCaseRequest(string? Question, Guid DocumentId, Guid ChunkId);

public record UpdateBenchmarkCaseRequest(string? Question);

public record SetBenchmarkCaseReviewedRequest(bool Reviewed);

public record StartBenchmarkRunRequest(string? Scope);

public record RejectedGeneratedQuestion(string? Question, string Reason);

public record GenerateBenchmarkCasesResponse(
    /// <summary>The id sent to n8n and echoed back for this generation (null when nothing was sent).</summary>
    Guid? GenerationId,
    /// <summary>How many chunks were sampled and sent to the generator.</summary>
    int ChunksSent,
    int QuestionsReturned,
    int CasesCreated,
    IReadOnlyList<RejectedGeneratedQuestion> Rejected,
    string? Message);

public record BenchmarkSourceDocumentResponse(Guid Id, string Title, string Category, int ChunkCount);

public record BenchmarkSourceChunkResponse(Guid Id, int ChunkIndex, string Preview, string Content);

/// <summary>One chunk production search returned (or, when <see cref="PassedThreshold"/> is false, one that made the
/// top-K but scored under the clinic's minimum similarity so production would have dropped it).</summary>
public record BenchmarkRetrievedChunk(
    int Rank,
    Guid DocumentId,
    Guid ChunkId,
    string Title,
    double Score,
    bool PassedThreshold,
    bool IsExpectedChunk,
    bool IsExpectedDocument,
    string Preview);

public record BenchmarkResultResponse(
    Guid Id,
    Guid RunId,
    Guid? CaseId,
    string Question,
    string Classification,
    Guid ExpectedDocumentId,
    Guid ExpectedChunkId,
    string? ExpectedDocumentTitle,
    string? ExpectedChunkPreview,
    int? ExpectedChunkRank,
    int? ExpectedDocumentBestRank,
    double? ExpectedChunkScore,
    bool ExpectedBelowThreshold,
    bool ChunkTop1Pass,
    bool ChunkTop3Pass,
    bool ChunkTop5Pass,
    bool DocumentTop1Pass,
    bool DocumentTop3Pass,
    bool DocumentTop5Pass,
    int ReturnedCount,
    int? LatencyMs,
    string? StaleReason,
    string? ErrorMessage,
    DateTimeOffset CreatedAt);

public record BenchmarkResultDetailResponse(
    BenchmarkResultResponse Result,
    IReadOnlyList<BenchmarkRetrievedChunk> Retrieved,
    /// <summary>The expected chunk's current full text; null if it no longer exists (then only the preview is known).</summary>
    string? ExpectedChunkContent,
    /// <summary>The run's settings snapshot, so a failure can be read against the settings it ran under.</summary>
    BenchmarkSettingsSnapshot RunSettings);

public record BenchmarkResultListResponse(IReadOnlyList<BenchmarkResultResponse> Items, int TotalCount);
