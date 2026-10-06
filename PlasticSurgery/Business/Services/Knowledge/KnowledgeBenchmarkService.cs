using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PlasticSurgery.Business.Contracts.Engines.KnowledgeBenchmark;
using PlasticSurgery.Business.Contracts.HttpClients.N8n;
using PlasticSurgery.Business.Contracts.Jobs;
using PlasticSurgery.Business.Contracts.Services.Knowledge;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Exceptions;
using PlasticSurgery.Entities.Dtos.Knowledge;
using PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Requests.KnowledgeBenchmark;
using PlasticSurgery.Entities.Responses.KnowledgeBenchmark;
using PlasticSurgery.Persistence.Contracts;
using PlasticSurgery.Persistence.Contracts.Knowledge;

namespace PlasticSurgery.Business.Services.Knowledge;

public partial class KnowledgeBenchmarkService : IKnowledgeBenchmarkService
{
    public const int GenerationSampleSize = 20;
    private const int MinSourceChunkChars = 80;
    private const int SamplePoolSize = 60;
    private const int MaxQuestionChars = 500;
    private const int PreviewChars = 300;
    private const int RetrievedPreviewChars = 240;
    private const int MaxConsecutiveErrors = 5;
    /// <summary>A run that has been pending/running longer than this is treated as dead so it can't block new runs forever.</summary>
    private static readonly TimeSpan StaleRunAge = TimeSpan.FromHours(2);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IKnowledgeBenchmarkRepository _benchmark;
    private readonly IKnowledgeDocumentRepository _documents;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IKnowledgeSearchService _search;           // the production retrieval engine
    private readonly IKnowledgeSettingsService _settings;
    private readonly IKnowledgeBenchmarkGeneratorClient _generator;
    private readonly IKnowledgeBenchmarkScorer _scorer;
    private readonly IKnowledgeBenchmarkRunQueue _queue;
    private readonly ILogger<KnowledgeBenchmarkService> _logger;

    public KnowledgeBenchmarkService(
        IKnowledgeBenchmarkRepository benchmark,
        IKnowledgeDocumentRepository documents,
        IUnitOfWork unitOfWork,
        IKnowledgeSearchService search,
        IKnowledgeSettingsService settings,
        IKnowledgeBenchmarkGeneratorClient generator,
        IKnowledgeBenchmarkScorer scorer,
        IKnowledgeBenchmarkRunQueue queue,
        ILogger<KnowledgeBenchmarkService> logger)
    {
        _benchmark = benchmark;
        _documents = documents;
        _unitOfWork = unitOfWork;
        _search = search;
        _settings = settings;
        _generator = generator;
        _scorer = scorer;
        _queue = queue;
        _logger = logger;
    }

    // ---------------------------------------------------------------------------------------------
    // Dashboard
    // ---------------------------------------------------------------------------------------------

    public async Task<BenchmarkDashboardResponse> GetDashboardAsync(Guid clinicId, CancellationToken ct = default)
    {
        await RefreshStaleAsync(clinicId, ct);

        var counts = await _benchmark.GetCaseCountsAsync(clinicId, ct);

        var latest = await _benchmark.GetLatestRunAsync(clinicId, completedOnly: false, ct);
        var latestCompleted = latest?.Status == BenchmarkRunStatus.Completed
            ? latest
            : await _benchmark.GetLatestRunAsync(clinicId, completedOnly: true, ct);

        await ExpireOldGenerationsAsync(clinicId, ct);
        var pendingGenerationId = await _benchmark.GetPendingGenerationIdAsync(clinicId, ct);
        var generationInProgress = pendingGenerationId is not null;

        var cutoff = DateTimeOffset.UtcNow - StaleRunAge;
        var inProgress = await _benchmark.HasRunInProgressAsync(clinicId, cutoff, ct);

        return new BenchmarkDashboardResponse(
            counts.Total, counts.Generated, counts.Manual, counts.Reviewed, counts.Stale,
            _generator.IsConfigured, inProgress, generationInProgress, pendingGenerationId,
            await CurrentSettingsSnapshotAsync(clinicId, ct),
            latest is null ? null : ToSummary(latest),
            latestCompleted is null ? null : ToSummary(latestCompleted));
    }

    // ---------------------------------------------------------------------------------------------
    // Cases
    // ---------------------------------------------------------------------------------------------

    public async Task<BenchmarkCaseListResponse> ListCasesAsync(Guid clinicId, BenchmarkCaseFilter filter, int skip, int take, CancellationToken ct = default)
    {
        await RefreshStaleAsync(clinicId, ct);

        var (page, total) = await _benchmark.ListCasesAsync(clinicId, (filter.View ?? "all").Trim().ToLowerInvariant(),
            filter.GenerationId, Math.Max(skip, 0), Math.Clamp(take, 1, 200), ct);

        var lastByCase = await LastResultsAsync(clinicId, page.Select(c => c.Id).ToList(), ct);
        return new BenchmarkCaseListResponse(
            page.Select(c => ToCaseResponse(c, lastByCase.GetValueOrDefault(c.Id))).ToList(), total);
    }

    public async Task<BenchmarkCaseDetailResponse?> GetCaseAsync(Guid clinicId, Guid id, CancellationToken ct = default)
    {
        await RefreshStaleAsync(clinicId, ct);

        var c = await _benchmark.GetCaseReadOnlyAsync(clinicId, id, ct);
        if (c is null) return null;

        var content = await LiveChunkContentAsync(clinicId, c.ExpectedChunkId, c.ExpectedDocumentId, ct);

        var latestResult = await _benchmark.GetLatestResultForCaseAsync(clinicId, id, ct);

        BenchmarkResultDetailResponse? detail = latestResult is null ? null : await BuildResultDetailAsync(clinicId, latestResult, ct);
        var last = latestResult is null ? null : ToLastResult(latestResult);
        return new BenchmarkCaseDetailResponse(ToCaseResponse(c, last), content, detail);
    }

    public async Task<BenchmarkCaseResponse> CreateManualCaseAsync(Guid clinicId, CreateManualBenchmarkCaseRequest request, CancellationToken ct = default)
    {
        var question = NormalizeQuestion(request.Question);
        if (string.IsNullOrEmpty(question)) throw new ArgumentException("Enter the patient question.");
        if (question.Length > MaxQuestionChars) throw new ArgumentException($"The question must be {MaxQuestionChars} characters or fewer.");

        var chunk = await _benchmark.GetActiveChunkAsync(clinicId, request.DocumentId, request.ChunkId, ct);
        if (chunk is null) throw new ArgumentException("Choose one of this clinic's active Knowledge Base chunks as the expected answer.");

        var now = DateTimeOffset.UtcNow;
        var entity = new KnowledgeRetrievalBenchmarkCase
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            Question = question,
            ExpectedDocumentId = chunk.DocumentId,
            ExpectedChunkId = chunk.ChunkId,
            CaseType = BenchmarkCaseType.Manual,
            IsReviewed = true, // a person wrote it — it is reviewed by definition
            ReviewedAt = now,
            SourceChunkHash = Sha256Hex(chunk.Content),
            SourceChunkPreview = Preview(chunk.Content, PreviewChars),
            SourceDocumentTitle = Truncate(chunk.DocumentTitle, 200),
            CreatedAt = now,
            UpdatedAt = now
        };

        _benchmark.AddCase(entity);
        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (DuplicateRecordException)
        {
            _benchmark.DetachCase(entity);
            throw new ArgumentException("A case with this question already exists for that chunk.");
        }
        return ToCaseResponse(entity, null);
    }

    public async Task<BenchmarkCaseResponse?> UpdateCaseQuestionAsync(Guid clinicId, Guid id, string? question, CancellationToken ct = default)
    {
        var text = NormalizeQuestion(question);
        if (string.IsNullOrEmpty(text)) throw new ArgumentException("The question can't be empty.");
        if (text.Length > MaxQuestionChars) throw new ArgumentException($"The question must be {MaxQuestionChars} characters or fewer.");

        var c = await _benchmark.GetCaseAsync(clinicId, id, ct);
        if (c is null) return null;

        c.Question = text;
        c.UpdatedAt = DateTimeOffset.UtcNow;
        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (DuplicateRecordException)
        {
            _benchmark.DetachCase(c);
            throw new ArgumentException("Another case for this chunk already has that question.");
        }

        var last = (await LastResultsAsync(clinicId, new List<Guid> { id }, ct)).GetValueOrDefault(id);
        return ToCaseResponse(c, last);
    }

    public async Task<BenchmarkCaseResponse?> SetReviewedAsync(Guid clinicId, Guid id, bool reviewed, CancellationToken ct = default)
    {
        var c = await _benchmark.GetCaseAsync(clinicId, id, ct);
        if (c is null) return null;

        c.IsReviewed = reviewed;
        c.ReviewedAt = reviewed ? DateTimeOffset.UtcNow : null;
        c.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        var last = (await LastResultsAsync(clinicId, new List<Guid> { id }, ct)).GetValueOrDefault(id);
        return ToCaseResponse(c, last);
    }

    public async Task<bool> DeleteCaseAsync(Guid clinicId, Guid id, CancellationToken ct = default)
    {
        // Results keep their own snapshot of the question; the FK is ON DELETE SET NULL, so run history survives.
        var deleted = await _benchmark.DeleteCaseAsync(clinicId, id, ct);
        return deleted > 0;
    }

    public Task<IReadOnlyList<BenchmarkSourceDocumentResponse>> ListSourceDocumentsAsync(Guid clinicId, CancellationToken ct = default) =>
        _benchmark.ListSourceDocumentsAsync(clinicId, ct);

    public async Task<IReadOnlyList<BenchmarkSourceChunkResponse>?> ListSourceChunksAsync(Guid clinicId, Guid documentId, CancellationToken ct = default)
    {
        var docExists = await _documents.ExistsAsync(clinicId, documentId, ct);
        if (!docExists) return null;

        var chunks = await _benchmark.ListDocumentChunksAsync(clinicId, documentId, ct);
        return chunks.Select(k => new BenchmarkSourceChunkResponse(k.Id, k.ChunkIndex, Preview(k.Content, 160), k.Content)).ToList();
    }

    // ---------------------------------------------------------------------------------------------
    // Runs
    // ---------------------------------------------------------------------------------------------

    public async Task<BenchmarkRunSummary> StartRunAsync(Guid clinicId, string? scope, Guid? generationId = null, CancellationToken ct = default)
    {
        var normalizedScope = string.IsNullOrWhiteSpace(scope) ? BenchmarkCaseScope.All : scope.Trim().ToLowerInvariant();
        if (!BenchmarkCaseScope.IsValid(normalizedScope)) throw new ArgumentException("Scope must be all, generated, reviewed or generation.");
        if (normalizedScope == BenchmarkCaseScope.Generation && generationId is null)
        {
            throw new ArgumentException("generationId is required when scope is \"generation\".");
        }
        if (normalizedScope != BenchmarkCaseScope.Generation && generationId is not null)
        {
            throw new ArgumentException("generationId is only valid with scope \"generation\".");
        }
        if (generationId is { } gid && !await _benchmark.GenerationExistsAsync(clinicId, gid, ct))
        {
            throw new ArgumentException("No such generation for this clinic.");
        }

        await RefreshStaleAsync(clinicId, ct);

        var cutoff = DateTimeOffset.UtcNow - StaleRunAge;
        if (await _benchmark.HasRunInProgressAsync(clinicId, cutoff, ct))
        {
            throw new BenchmarkRunInProgressException();
        }

        var (total, stale) = await _benchmark.CountCasesInScopeAsync(clinicId, normalizedScope, generationId, ct);
        if (total == 0)
        {
            throw new ArgumentException(normalizedScope switch
            {
                BenchmarkCaseScope.Reviewed => "There are no reviewed cases yet — mark some cases as reviewed first.",
                BenchmarkCaseScope.Generation => "This generation has no cases left (they may have been deleted).",
                _ => "There are no benchmark cases in this scope yet — generate some first."
            });
        }
        if (total - stale == 0) throw new ArgumentException("Every case in this scope is stale (its Knowledge Base chunk changed). Generate new cases first.");

        var snapshot = await CurrentSettingsSnapshotAsync(clinicId, ct);
        var run = new KnowledgeRetrievalBenchmarkRun
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            CaseScope = normalizedScope,
            GenerationId = generationId,
            Status = BenchmarkRunStatus.Pending,
            TotalCases = total,
            StaleCases = stale,
            EmbeddingModel = snapshot.EmbeddingModel,
            VectorDimension = snapshot.VectorDimension,
            ChunkSizeTokens = snapshot.ChunkSizeTokens,
            ChunkOverlapTokens = snapshot.ChunkOverlapTokens,
            SimilarityMethod = snapshot.SimilarityMethod,
            TopK = snapshot.TopK,
            MinimumSimilarity = snapshot.MinimumSimilarity,
            IndexedChunkCount = snapshot.IndexedChunkCount,
            AvgChunkChars = snapshot.AvgChunkChars,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _benchmark.AddRun(run);
        await _unitOfWork.SaveChangesAsync(ct);

        _queue.Enqueue(run.Id);
        return ToSummary(run);
    }

    public async Task ExecuteRunAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await _benchmark.GetRunAsync(runId, ct);
        if (run is null || run.Status != BenchmarkRunStatus.Pending) return;
        var clinicId = run.ClinicId;

        try
        {
            run.Status = BenchmarkRunStatus.Running;
            run.StartedAt = DateTimeOffset.UtcNow;
            await _unitOfWork.SaveChangesAsync(ct);

            // Cases may have gone stale between queueing and starting.
            await RefreshStaleAsync(clinicId, ct);
            var cases = await _benchmark.ListCasesInScopeAsync(clinicId, run.CaseScope, run.GenerationId, ct);

            run.TotalCases = cases.Count;
            run.StaleCases = cases.Count(c => c.IsStale);

            var scores = new List<BenchmarkScore>();
            var latencies = new List<long>();
            var consecutiveErrors = 0;
            string? lastError = null;
            var errorCases = 0;
            var processed = 0;
            var aborted = false;

            foreach (var c in cases)
            {
                ct.ThrowIfCancellationRequested();

                KnowledgeRetrievalBenchmarkResult result;
                if (c.IsStale)
                {
                    // Stale cases are recorded but never scored — they are not retrieval failures.
                    result = NewResult(run, c, BenchmarkClassification.StaleCase);
                    result.StaleReason = c.StaleReason;
                }
                else
                {
                    var (evaluated, score) = await EvaluateCaseAsync(run, c, ct);
                    result = evaluated;
                    if (score is null)
                    {
                        errorCases++;
                        consecutiveErrors++;
                        lastError = result.ErrorMessage;
                    }
                    else
                    {
                        consecutiveErrors = 0;
                        scores.Add(score);
                        latencies.Add(result.LatencyMs ?? 0);
                    }
                }

                _benchmark.AddResult(result);
                processed++;
                run.ProcessedCases = processed;
                run.ErrorCases = errorCases;
                run.ScoredCases = scores.Count;
                await _unitOfWork.SaveChangesAsync(ct);
                _benchmark.DetachResult(result);

                if (consecutiveErrors >= MaxConsecutiveErrors)
                {
                    aborted = true;
                    break;
                }
            }

            var metrics = _scorer.Summarize(scores);
            run.ScoredCases = metrics.ScoredCases;
            run.ChunkTop1Accuracy = metrics.ChunkTop1;
            run.ChunkTop3Accuracy = metrics.ChunkTop3;
            run.ChunkTop5Accuracy = metrics.ChunkTop5;
            run.ChunkMrr = metrics.ChunkMrr;
            run.DocumentTop1Accuracy = metrics.DocumentTop1;
            run.DocumentTop3Accuracy = metrics.DocumentTop3;
            run.DocumentTop5Accuracy = metrics.DocumentTop5;
            run.DocumentMrr = metrics.DocumentMrr;
            run.AverageLatencyMs = latencies.Count == 0 ? null : latencies.Average();
            run.CompletedAt = DateTimeOffset.UtcNow;

            if (aborted)
            {
                run.Status = BenchmarkRunStatus.Failed;
                run.ErrorSummary = $"Stopped after {MaxConsecutiveErrors} retrieval errors in a row: {Truncate(lastError, 300)}";
            }
            else
            {
                run.Status = BenchmarkRunStatus.Completed;
                if (errorCases > 0) run.ErrorSummary = $"{errorCases} case(s) errored and were not scored: {Truncate(lastError, 300)}";
            }
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw; // shutting down — startup recovery marks the run interrupted
        }
        catch (Exception ex)
        {
            // A benchmark failure is recorded on the run and goes no further: production retrieval is untouched.
            _logger.LogError(ex, "Benchmark run {RunId} failed.", runId);
            try
            {
                _unitOfWork.DiscardChanges();
                var failed = await _benchmark.GetRunAsync(runId, CancellationToken.None);
                if (failed is not null)
                {
                    failed.Status = BenchmarkRunStatus.Failed;
                    failed.CompletedAt = DateTimeOffset.UtcNow;
                    failed.ErrorSummary = Truncate($"The benchmark run failed: {ex.Message}", 500);
                    await _unitOfWork.SaveChangesAsync(CancellationToken.None);
                }
            }
            catch (Exception inner)
            {
                _logger.LogError(inner, "Could not record the failure of benchmark run {RunId}.", runId);
            }
        }
    }

    /// <summary>Sends the case's question through the production retrieval engine and scores the ranked output.
    /// A retrieval failure (e.g. the embedding provider is down) is an ERROR result — not counted as a miss.</summary>
    private async Task<(KnowledgeRetrievalBenchmarkResult Result, BenchmarkScore? Score)> EvaluateCaseAsync(
        KnowledgeRetrievalBenchmarkRun run, KnowledgeRetrievalBenchmarkCase c, CancellationToken ct)
    {
        var result = NewResult(run, c, BenchmarkClassification.Miss);

        IReadOnlyList<RankedKnowledgeChunk> candidates;
        var sw = Stopwatch.StartNew();
        try
        {
            // Same service, same clinic settings (Top K, minimum similarity, model) as search_clinic_knowledge.
            candidates = await _search.SearchRankedAsync(run.ClinicId, c.Question, null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.ResultClassification = BenchmarkClassification.Error;
            result.ErrorMessage = Truncate(ex.Message, 500);
            return (result, null);
        }
        sw.Stop();

        // Strict production semantics: only chunks that CLEARED the minimum similarity count as returned.
        var returned = candidates.Where(x => x.MeetsMinimumSimilarity)
            .Select(x => new BenchmarkRetrievedItem(x.DocumentId, x.ChunkId)).ToList();
        var score = _scorer.Score(c.ExpectedDocumentId, c.ExpectedChunkId, returned);

        var expectedCandidate = candidates.FirstOrDefault(x => x.ChunkId == c.ExpectedChunkId && x.DocumentId == c.ExpectedDocumentId);

        result.ResultClassification = score.Classification;
        result.ExpectedChunkRank = score.ExpectedChunkRank;
        result.ExpectedDocumentBestRank = score.ExpectedDocumentBestRank;
        result.ExpectedChunkScore = expectedCandidate is null ? null : Math.Round(expectedCandidate.Score, 4);
        result.ExpectedBelowThreshold = expectedCandidate is { MeetsMinimumSimilarity: false };
        result.ChunkTop1Pass = score.ChunkTop1;
        result.ChunkTop3Pass = score.ChunkTop3;
        result.ChunkTop5Pass = score.ChunkTop5;
        result.DocumentTop1Pass = score.DocumentTop1;
        result.DocumentTop3Pass = score.DocumentTop3;
        result.DocumentTop5Pass = score.DocumentTop5;
        result.ReturnedCount = returned.Count;
        result.LatencyMs = (int)sw.ElapsedMilliseconds;
        result.RetrievedJson = JsonSerializer.Serialize(
            candidates.Select((x, i) => new RetrievedRow(
                i + 1, x.DocumentId, x.ChunkId, x.Title, Math.Round(x.Score, 4), x.MeetsMinimumSimilarity, Preview(x.Content, RetrievedPreviewChars))),
            JsonOptions);
        return (result, score);
    }

    private static KnowledgeRetrievalBenchmarkResult NewResult(KnowledgeRetrievalBenchmarkRun run, KnowledgeRetrievalBenchmarkCase c, string classification) => new()
    {
        Id = Guid.NewGuid(),
        ClinicId = run.ClinicId,
        BenchmarkRunId = run.Id,
        BenchmarkCaseId = c.Id,
        Question = c.Question,
        ExpectedDocumentId = c.ExpectedDocumentId,
        ExpectedChunkId = c.ExpectedChunkId,
        ExpectedDocumentTitle = c.SourceDocumentTitle,
        ExpectedChunkPreview = c.SourceChunkPreview,
        GenerationId = c.GenerationId,
        ResultClassification = classification,
        CreatedAt = DateTimeOffset.UtcNow
    };

    public async Task<IReadOnlyList<BenchmarkRunSummary>> ListRunsAsync(Guid clinicId, int take, CancellationToken ct = default) =>
        (await _benchmark.ListRunsAsync(clinicId, Math.Clamp(take, 1, 100), ct))
        .Select(ToSummary).ToList();

    public async Task<BenchmarkRunSummary?> GetRunAsync(Guid clinicId, Guid id, CancellationToken ct = default)
    {
        var run = await _benchmark.GetRunReadOnlyAsync(clinicId, id, ct);
        return run is null ? null : ToSummary(run);
    }

    public async Task<BenchmarkResultListResponse?> ListResultsAsync(Guid clinicId, Guid runId, string? classification, int skip, int take, CancellationToken ct = default)
    {
        if (!await _benchmark.RunExistsAsync(clinicId, runId, ct)) return null;

        var wanted = string.IsNullOrWhiteSpace(classification) ? null : classification.Trim().ToUpperInvariant();
        var (rows, total) = await _benchmark.ListRunResultsAsync(clinicId, runId, wanted, Math.Max(skip, 0), Math.Clamp(take, 1, 500), ct);

        return new BenchmarkResultListResponse(rows.Select(ToResultResponse).ToList(), total);
    }

    public async Task<BenchmarkResultDetailResponse?> GetResultAsync(Guid clinicId, Guid runId, Guid resultId, CancellationToken ct = default)
    {
        var result = await _benchmark.GetResultAsync(clinicId, runId, resultId, ct);
        return result is null ? null : await BuildResultDetailAsync(clinicId, result, ct);
    }

    public async Task<IReadOnlyList<Guid>> RecoverInterruptedRunsAsync(CancellationToken ct = default)
    {
        var interrupted = await _benchmark.ListRunsInStatusAsync(BenchmarkRunStatus.Running, ct);
        foreach (var run in interrupted)
        {
            run.Status = BenchmarkRunStatus.Failed;
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.ErrorSummary = "The benchmark run was interrupted by an application restart. Run it again.";
        }
        await _unitOfWork.SaveChangesAsync(ct);

        return await _benchmark.ListRunIdsInStatusAsync(BenchmarkRunStatus.Pending, ct);
    }

    // ---------------------------------------------------------------------------------------------
    // Stale detection
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Re-checks every case of the clinic against the live Knowledge Base and flags the ones whose expected chunk can no
    /// longer be relied on. A case is stale when: the expected chunk id no longer exists (or now belongs to a different
    /// document) → chunk_missing; its text no longer hashes to source_chunk_hash → chunk_changed; or its document is
    /// inactive (search never returns it) → document_inactive. Stale cases are excluded from scoring — never counted as
    /// retrieval failures.
    /// Self-healing: chunks get new ids when a document is re-saved, so for a missing/changed chunk we look for an ACTIVE
    /// chunk in the same clinic whose text hashes to the case's source hash. If exactly one exists, the identical text
    /// was simply rebuilt under a new id and the case is re-linked to it (still verified by hash) instead of going stale.
    /// Best-effort: a failure here is logged and never blocks the caller.
    /// </summary>
    internal async Task RefreshStaleAsync(Guid clinicId, CancellationToken ct)
    {
        try
        {
            var cases = await _benchmark.ListAllCasesAsync(clinicId, ct);
            if (cases.Count == 0) return;

            var chunkIds = cases.Select(c => c.ExpectedChunkId).Distinct().ToList();
            var liveChunks = (await _benchmark.ListChunksAsync(clinicId, chunkIds, ct)).ToDictionary(k => k.ChunkId);
            var docs = (await _benchmark.ListDocumentStatesAsync(clinicId, ct)).ToDictionary(d => d.DocumentId);

            Dictionary<string, List<(Guid ChunkId, Guid DocumentId)>>? hashIndex = null;
            async Task<Dictionary<string, List<(Guid ChunkId, Guid DocumentId)>>> HashIndexAsync()
            {
                if (hashIndex is not null) return hashIndex;
                hashIndex = new();
                var active = await _benchmark.ListActiveChunksAsync(clinicId, null, ct);
                foreach (var k in active)
                {
                    var h = Sha256Hex(k.Content);
                    if (!hashIndex.TryGetValue(h, out var list)) hashIndex[h] = list = new();
                    list.Add((k.ChunkId, k.DocumentId));
                }
                return hashIndex;
            }

            var dirty = false;
            foreach (var c in cases)
            {
                string? reason = null;
                liveChunks.TryGetValue(c.ExpectedChunkId, out var live);
                if (live is null || live.DocumentId != c.ExpectedDocumentId) reason = BenchmarkStaleReason.ChunkMissing;
                else if (c.SourceChunkHash is not null && Sha256Hex(live.Content) != c.SourceChunkHash) reason = BenchmarkStaleReason.ChunkChanged;
                else if (!docs.TryGetValue(c.ExpectedDocumentId, out var d) || !d.IsActive) reason = BenchmarkStaleReason.DocumentInactive;

                if (reason is BenchmarkStaleReason.ChunkMissing or BenchmarkStaleReason.ChunkChanged && c.SourceChunkHash is not null)
                {
                    var matches = (await HashIndexAsync()).GetValueOrDefault(c.SourceChunkHash);
                    if (matches is { Count: 1 })
                    {
                        c.ExpectedChunkId = matches[0].ChunkId;
                        c.ExpectedDocumentId = matches[0].DocumentId;
                        if (docs.TryGetValue(c.ExpectedDocumentId, out var relinkedDoc)) c.SourceDocumentTitle = Truncate(relinkedDoc.Title, 200);
                        reason = null;
                        dirty = true;
                    }
                }

                if (reason is null && docs.TryGetValue(c.ExpectedDocumentId, out var doc) && c.SourceDocumentTitle != Truncate(doc.Title, 200))
                {
                    c.SourceDocumentTitle = Truncate(doc.Title, 200); // keep the displayed title current after a rename
                    dirty = true;
                }

                var stale = reason is not null;
                if (c.IsStale != stale || c.StaleReason != reason)
                {
                    c.IsStale = stale;
                    c.StaleReason = reason;
                    dirty = true;
                }
            }

            if (dirty) await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Benchmark stale-case refresh failed for clinic {ClinicId}; continuing with stored flags.", clinicId);
            // Drop only the unsaved case edits — the caller (e.g. a running benchmark) may be tracking other entities.
            _benchmark.DetachAllCases();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    private sealed record SampledChunk(Guid ChunkId, Guid DocumentId, string Title, string Content);

    /// <summary>Picks up to <see cref="GenerationSampleSize"/> ACTIVE chunks of this clinic that have no case yet. Spread
    /// across documents (one random chunk per document first, then a second, …) so one large scraped document can't
    /// dominate, and random, so repeated generations don't keep choosing the same chunks. Chunks whose text already has a
    /// case (e.g. rebuilt under a new id) are skipped too.</summary>
    private async Task<List<SampledChunk>> SampleSourceChunksAsync(Guid clinicId, CancellationToken ct)
    {
        var pool = await _benchmark.SampleUncoveredChunksAsync(clinicId, MinSourceChunkChars, SamplePoolSize, ct);

        var coveredHashes = (await _benchmark.ListCoveredSourceHashesAsync(clinicId, ct)).ToHashSet();

        var picked = new List<SampledChunk>();
        var pickedHashes = new HashSet<string>();
        foreach (var row in pool)
        {
            var hash = Sha256Hex(row.Content);
            if (coveredHashes.Contains(hash) || !pickedHashes.Add(hash)) continue;
            picked.Add(new SampledChunk(row.ChunkId, row.DocumentId, row.DocumentTitle ?? string.Empty, row.Content));
            if (picked.Count == GenerationSampleSize) break;
        }
        return picked;
    }

    private async Task<BenchmarkSettingsSnapshot> CurrentSettingsSnapshotAsync(Guid clinicId, CancellationToken ct)
    {
        var s = await _settings.GetAsync(clinicId, ct);
        var stats = await _benchmark.GetActiveChunkStatsAsync(clinicId, ct);
        var count = stats.Count;
        var avg = stats.AverageChars;
        return new BenchmarkSettingsSnapshot(
            s.EmbeddingModel, s.VectorDimension, s.ChunkSizeTokens, s.ChunkOverlapTokens, s.SimilarityMethod,
            s.TopK, s.MinimumSimilarity, count, avg is null ? null : Math.Round(avg.Value, 0));
    }

    private async Task<Dictionary<Guid, BenchmarkCaseLastResult>> LastResultsAsync(Guid clinicId, List<Guid> caseIds, CancellationToken ct)
    {
        if (caseIds.Count == 0) return new();
        var rows = await _benchmark.ListResultsForCasesAsync(clinicId, caseIds, ct);

        return rows.GroupBy(r => r.BenchmarkCaseId!.Value).ToDictionary(
            g => g.Key,
            g => { var r = g.First(); return new BenchmarkCaseLastResult(r.Id, r.BenchmarkRunId, r.ResultClassification, r.ExpectedChunkRank, r.ExpectedDocumentBestRank, r.CreatedAt); });
    }

    private Task<string?> LiveChunkContentAsync(Guid clinicId, Guid chunkId, Guid documentId, CancellationToken ct) =>
        _benchmark.GetChunkContentAsync(clinicId, chunkId, documentId, ct);

    private async Task<BenchmarkResultDetailResponse> BuildResultDetailAsync(Guid clinicId, KnowledgeRetrievalBenchmarkResult result, CancellationToken ct)
    {
        var run = await _benchmark.GetRunReadOnlyAsync(clinicId, result.BenchmarkRunId, ct)
                  ?? throw new InvalidOperationException("Benchmark run not found.");

        var retrieved = new List<BenchmarkRetrievedChunk>();
        if (!string.IsNullOrEmpty(result.RetrievedJson))
        {
            var rows = JsonSerializer.Deserialize<List<RetrievedRow>>(result.RetrievedJson, JsonOptions) ?? new();
            retrieved = rows.Select(r => new BenchmarkRetrievedChunk(
                r.Rank, r.DocumentId, r.ChunkId, r.Title, r.Score, r.PassedThreshold,
                IsExpectedChunk: r.ChunkId == result.ExpectedChunkId && r.DocumentId == result.ExpectedDocumentId,
                IsExpectedDocument: r.DocumentId == result.ExpectedDocumentId,
                r.Preview)).ToList();
        }

        var content = await LiveChunkContentAsync(clinicId, result.ExpectedChunkId, result.ExpectedDocumentId, ct);
        return new BenchmarkResultDetailResponse(ToResultResponse(result), retrieved, content, ToSummary(run).Settings);
    }

    /// <summary>The persisted shape of one retrieved candidate inside knowledge_retrieval_benchmark_results.retrieved_json.</summary>
    internal sealed record RetrievedRow(int Rank, Guid DocumentId, Guid ChunkId, string Title, double Score, bool PassedThreshold, string Preview);

    private static BenchmarkCaseResponse ToCaseResponse(KnowledgeRetrievalBenchmarkCase c, BenchmarkCaseLastResult? last) => new(
        c.Id, c.Question, c.CaseType, c.IsReviewed, c.ReviewedAt, c.GenerationId, c.IsStale, c.StaleReason,
        c.IsStale ? BenchmarkStaleReason.Label(c.StaleReason) : null,
        c.ExpectedDocumentId, c.ExpectedChunkId, c.SourceDocumentTitle, c.SourceChunkPreview,
        c.CreatedAt, c.UpdatedAt, last);

    private static BenchmarkCaseLastResult ToLastResult(KnowledgeRetrievalBenchmarkResult r) =>
        new(r.Id, r.BenchmarkRunId, r.ResultClassification, r.ExpectedChunkRank, r.ExpectedDocumentBestRank, r.CreatedAt);

    private static BenchmarkResultResponse ToResultResponse(KnowledgeRetrievalBenchmarkResult r) => new(
        r.Id, r.BenchmarkRunId, r.BenchmarkCaseId, r.Question, r.ResultClassification,
        r.ExpectedDocumentId, r.ExpectedChunkId, r.ExpectedDocumentTitle, r.ExpectedChunkPreview,
        r.ExpectedChunkRank, r.ExpectedDocumentBestRank, r.ExpectedChunkScore, r.ExpectedBelowThreshold,
        r.ChunkTop1Pass, r.ChunkTop3Pass, r.ChunkTop5Pass, r.DocumentTop1Pass, r.DocumentTop3Pass, r.DocumentTop5Pass,
        r.ReturnedCount, r.LatencyMs, r.StaleReason, r.ErrorMessage, r.CreatedAt);

    private static BenchmarkRunSummary ToSummary(KnowledgeRetrievalBenchmarkRun r) => new(
        r.Id, r.CaseScope, r.GenerationId, r.Status, r.StartedAt, r.CompletedAt, r.CreatedAt,
        r.TotalCases, r.ProcessedCases, r.ScoredCases, r.StaleCases, r.ErrorCases,
        r.ChunkTop1Accuracy, r.ChunkTop3Accuracy, r.ChunkTop5Accuracy, r.ChunkMrr,
        r.DocumentTop1Accuracy, r.DocumentTop3Accuracy, r.DocumentTop5Accuracy, r.DocumentMrr,
        r.AverageLatencyMs,
        new BenchmarkSettingsSnapshot(
            r.EmbeddingModel, r.VectorDimension, r.ChunkSizeTokens, r.ChunkOverlapTokens, r.SimilarityMethod,
            r.TopK, r.MinimumSimilarity, r.IndexedChunkCount, r.AvgChunkChars),
        r.ErrorSummary);

    private static string NormalizeQuestion(string? question) =>
        string.Join(' ', (question ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Preview(string text, int max)
    {
        var flat = NormalizeQuestion(text); // collapses whitespace/newlines
        return flat.Length <= max ? flat : flat[..max].TrimEnd() + "…";
    }

    private static string Truncate(string? text, int max) =>
        string.IsNullOrEmpty(text) ? string.Empty : (text.Length <= max ? text : text[..max]);

    internal static string Sha256Hex(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}
