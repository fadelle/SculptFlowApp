using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Integrations.Knowledge.Benchmark;

/// <summary>A run was requested while another is still pending/running for the clinic.</summary>
public class BenchmarkRunInProgressException : InvalidOperationException
{
    public BenchmarkRunInProgressException() : base("A benchmark run is already in progress for this clinic — wait for it to finish.") { }
}

/// <summary>Which slice of the case list to return. "all" | "generated" | "manual" | "reviewed" | "unreviewed" | "stale".</summary>
public record BenchmarkCaseFilter(string? View = null, Guid? GenerationId = null);

/// <summary>
/// Orchestrates the Knowledge Retrieval Benchmark: sampling source chunks, validating generated questions, storing
/// cases, detecting stale ones, executing runs and reading results back. It is a CLIENT of the production retrieval
/// engine (<see cref="IKnowledgeSearchService"/>) — it never owns or duplicates retrieval — and nothing in the
/// production Knowledge Base depends on it. Every method is scoped to a clinic id that the caller took from
/// CurrentClinicContext (or, for <see cref="ExecuteRunAsync"/>, from the run row that was created that way).
/// </summary>
public interface IKnowledgeBenchmarkService
{
    Task<BenchmarkDashboardResponse> GetDashboardAsync(Guid clinicId, CancellationToken ct = default);

    Task<BenchmarkCaseListResponse> ListCasesAsync(Guid clinicId, BenchmarkCaseFilter filter, int skip, int take, CancellationToken ct = default);
    Task<BenchmarkCaseDetailResponse?> GetCaseAsync(Guid clinicId, Guid id, CancellationToken ct = default);

    /// <summary>Samples up to 20 active chunks (not already covered by a case) from THIS clinic, records a pending generation and
    /// hands the chunks to the n8n generator, then returns immediately (202). n8n calls back with the questions —
    /// see <see cref="ReceiveGenerationResultAsync"/>. A workflow that answers synchronously is processed right away.</summary>
    Task<StartBenchmarkGenerationResponse> StartGenerationAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Applies the questions n8n sent for a generation (its callback, or a synchronous reply). The clinic and the set of
    /// chunks that were sent come from the STORED generation — never from the caller — and every returned id is validated against
    /// them and the live Knowledge Base. Idempotent: a second delivery is reported as already processed.</summary>
    Task<GenerationReceiveResult> ReceiveGenerationResultAsync(Guid generationId, GeneratorResponse reply, string? rawBody, bool requireEcho, CancellationToken ct = default);

    /// <summary>"Stop generating": marks THIS clinic's pending generation cancelled so Generate is free again and n8n's late reply is
    /// refused. It cannot halt n8n's own run. A generation that already finished is reported as such, not changed.</summary>
    Task<GenerationCancelResult> CancelGenerationAsync(Guid clinicId, Guid generationId, CancellationToken ct = default);

    Task<IReadOnlyList<BenchmarkGenerationSummary>> ListGenerationsAsync(Guid clinicId, int take, CancellationToken ct = default);
    Task<BenchmarkGenerationDetail?> GetGenerationAsync(Guid clinicId, Guid id, CancellationToken ct = default);
    /// <summary>Scores of a run split by the generation each case came from. Null when the run isn't this clinic's.</summary>
    Task<IReadOnlyList<BenchmarkRunGenerationBreakdown>?> GetRunGenerationBreakdownAsync(Guid clinicId, Guid runId, CancellationToken ct = default);

    Task<BenchmarkCaseResponse> CreateManualCaseAsync(Guid clinicId, CreateManualBenchmarkCaseRequest request, CancellationToken ct = default);
    Task<BenchmarkCaseResponse?> UpdateCaseQuestionAsync(Guid clinicId, Guid id, string? question, CancellationToken ct = default);
    Task<BenchmarkCaseResponse?> SetReviewedAsync(Guid clinicId, Guid id, bool reviewed, CancellationToken ct = default);
    Task<bool> DeleteCaseAsync(Guid clinicId, Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<BenchmarkSourceDocumentResponse>> ListSourceDocumentsAsync(Guid clinicId, CancellationToken ct = default);
    /// <summary>Null when the document isn't one of this clinic's.</summary>
    Task<IReadOnlyList<BenchmarkSourceChunkResponse>?> ListSourceChunksAsync(Guid clinicId, Guid documentId, CancellationToken ct = default);

    /// <summary>Creates a pending run for the scope ("all" | "generated" | "reviewed" | "generation") and queues it for
    /// the background worker. "generation" requires <paramref name="generationId"/> (a generation of THIS clinic) and
    /// scores only that batch's cases, whatever their type/reviewed state. Throws
    /// <see cref="BenchmarkRunInProgressException"/> if one is already active.</summary>
    Task<BenchmarkRunSummary> StartRunAsync(Guid clinicId, string? scope, Guid? generationId = null, CancellationToken ct = default);

    /// <summary>Executes a queued run (called by the background worker). Never throws for a benchmark failure — it
    /// records it on the run instead.</summary>
    Task ExecuteRunAsync(Guid runId, CancellationToken ct = default);

    Task<IReadOnlyList<BenchmarkRunSummary>> ListRunsAsync(Guid clinicId, int take, CancellationToken ct = default);
    Task<BenchmarkRunSummary?> GetRunAsync(Guid clinicId, Guid id, CancellationToken ct = default);
    /// <summary>Null when the run isn't one of this clinic's.</summary>
    Task<BenchmarkResultListResponse?> ListResultsAsync(Guid clinicId, Guid runId, string? classification, int skip, int take, CancellationToken ct = default);
    Task<BenchmarkResultDetailResponse?> GetResultAsync(Guid clinicId, Guid runId, Guid resultId, CancellationToken ct = default);

    /// <summary>Startup recovery: marks runs a previous process left running as failed and returns the pending run ids
    /// to queue again. Maintenance only — not user-facing, so not clinic-scoped.</summary>
    Task<IReadOnlyList<Guid>> RecoverInterruptedRunsAsync(CancellationToken ct = default);
}

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

    private readonly ApplicationDbContext _db;
    private readonly IKnowledgeSearchService _search;           // the production retrieval engine
    private readonly IKnowledgeSettingsService _settings;
    private readonly IKnowledgeBenchmarkGeneratorClient _generator;
    private readonly IKnowledgeBenchmarkScorer _scorer;
    private readonly IKnowledgeBenchmarkRunQueue _queue;
    private readonly ILogger<KnowledgeBenchmarkService> _logger;

    public KnowledgeBenchmarkService(
        ApplicationDbContext db,
        IKnowledgeSearchService search,
        IKnowledgeSettingsService settings,
        IKnowledgeBenchmarkGeneratorClient generator,
        IKnowledgeBenchmarkScorer scorer,
        IKnowledgeBenchmarkRunQueue queue,
        ILogger<KnowledgeBenchmarkService> logger)
    {
        _db = db;
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

        var counts = await _db.KnowledgeBenchmarkCases.AsNoTracking()
            .Where(c => c.ClinicId == clinicId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Generated = g.Count(c => c.CaseType == BenchmarkCaseType.Generated),
                Manual = g.Count(c => c.CaseType == BenchmarkCaseType.Manual),
                Reviewed = g.Count(c => c.IsReviewed),
                Stale = g.Count(c => c.IsStale)
            })
            .FirstOrDefaultAsync(ct);

        var latest = await _db.KnowledgeBenchmarkRuns.AsNoTracking()
            .Where(r => r.ClinicId == clinicId).OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(ct);
        var latestCompleted = latest?.Status == BenchmarkRunStatus.Completed
            ? latest
            : await _db.KnowledgeBenchmarkRuns.AsNoTracking()
                .Where(r => r.ClinicId == clinicId && r.Status == BenchmarkRunStatus.Completed)
                .OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(ct);

        await ExpireOldGenerationsAsync(clinicId, ct);
        var pendingGenerationId = await _db.KnowledgeBenchmarkGenerations.AsNoTracking()
            .Where(g => g.ClinicId == clinicId && g.Status == BenchmarkGenerationStatus.Pending)
            .OrderByDescending(g => g.CreatedAt).Select(g => (Guid?)g.Id).FirstOrDefaultAsync(ct);
        var generationInProgress = pendingGenerationId is not null;

        var cutoff = DateTimeOffset.UtcNow - StaleRunAge;
        var inProgress = await _db.KnowledgeBenchmarkRuns.AsNoTracking().AnyAsync(r =>
            r.ClinicId == clinicId && r.CreatedAt > cutoff
            && (r.Status == BenchmarkRunStatus.Pending || r.Status == BenchmarkRunStatus.Running), ct);

        return new BenchmarkDashboardResponse(
            counts?.Total ?? 0, counts?.Generated ?? 0, counts?.Manual ?? 0, counts?.Reviewed ?? 0, counts?.Stale ?? 0,
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

        var query = _db.KnowledgeBenchmarkCases.AsNoTracking().Where(c => c.ClinicId == clinicId);
        query = (filter.View ?? "all").Trim().ToLowerInvariant() switch
        {
            "generated" => query.Where(c => c.CaseType == BenchmarkCaseType.Generated),
            "manual" => query.Where(c => c.CaseType == BenchmarkCaseType.Manual),
            "reviewed" => query.Where(c => c.IsReviewed),
            "unreviewed" => query.Where(c => !c.IsReviewed),
            "stale" => query.Where(c => c.IsStale),
            _ => query
        };

        if (filter.GenerationId is { } generationFilter) query = query.Where(c => c.GenerationId == generationFilter);

        var total = await query.CountAsync(ct);
        var page = await query.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id)
            .Skip(Math.Max(skip, 0)).Take(Math.Clamp(take, 1, 200)).ToListAsync(ct);

        var lastByCase = await LastResultsAsync(clinicId, page.Select(c => c.Id).ToList(), ct);
        return new BenchmarkCaseListResponse(
            page.Select(c => ToCaseResponse(c, lastByCase.GetValueOrDefault(c.Id))).ToList(), total);
    }

    public async Task<BenchmarkCaseDetailResponse?> GetCaseAsync(Guid clinicId, Guid id, CancellationToken ct = default)
    {
        await RefreshStaleAsync(clinicId, ct);

        var c = await _db.KnowledgeBenchmarkCases.AsNoTracking().FirstOrDefaultAsync(x => x.ClinicId == clinicId && x.Id == id, ct);
        if (c is null) return null;

        var content = await LiveChunkContentAsync(clinicId, c.ExpectedChunkId, c.ExpectedDocumentId, ct);

        var latestResult = await _db.KnowledgeBenchmarkResults.AsNoTracking()
            .Where(r => r.ClinicId == clinicId && r.BenchmarkCaseId == id)
            .OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(ct);

        BenchmarkResultDetailResponse? detail = latestResult is null ? null : await BuildResultDetailAsync(clinicId, latestResult, ct);
        var last = latestResult is null ? null : ToLastResult(latestResult);
        return new BenchmarkCaseDetailResponse(ToCaseResponse(c, last), content, detail);
    }

    public async Task<BenchmarkCaseResponse> CreateManualCaseAsync(Guid clinicId, CreateManualBenchmarkCaseRequest request, CancellationToken ct = default)
    {
        var question = NormalizeQuestion(request.Question);
        if (string.IsNullOrEmpty(question)) throw new ArgumentException("Enter the patient question.");
        if (question.Length > MaxQuestionChars) throw new ArgumentException($"The question must be {MaxQuestionChars} characters or fewer.");

        var chunk = await _db.KnowledgeChunks.AsNoTracking()
            .Where(k => k.ClinicId == clinicId && k.Id == request.ChunkId && k.KnowledgeDocumentId == request.DocumentId
                        && k.Document!.ClinicId == clinicId && k.Document.IsActive)
            .Select(k => new { k.Id, k.KnowledgeDocumentId, k.Content, Title = k.Document!.Title })
            .FirstOrDefaultAsync(ct);
        if (chunk is null) throw new ArgumentException("Choose one of this clinic's active Knowledge Base chunks as the expected answer.");

        var now = DateTimeOffset.UtcNow;
        var entity = new KnowledgeRetrievalBenchmarkCase
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            Question = question,
            ExpectedDocumentId = chunk.KnowledgeDocumentId,
            ExpectedChunkId = chunk.Id,
            CaseType = BenchmarkCaseType.Manual,
            IsReviewed = true, // a person wrote it — it is reviewed by definition
            ReviewedAt = now,
            SourceChunkHash = Sha256Hex(chunk.Content),
            SourceChunkPreview = Preview(chunk.Content, PreviewChars),
            SourceDocumentTitle = Truncate(chunk.Title, 200),
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.KnowledgeBenchmarkCases.Add(entity);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            _db.Entry(entity).State = EntityState.Detached;
            throw new ArgumentException("A case with this question already exists for that chunk.");
        }
        return ToCaseResponse(entity, null);
    }

    public async Task<BenchmarkCaseResponse?> UpdateCaseQuestionAsync(Guid clinicId, Guid id, string? question, CancellationToken ct = default)
    {
        var text = NormalizeQuestion(question);
        if (string.IsNullOrEmpty(text)) throw new ArgumentException("The question can't be empty.");
        if (text.Length > MaxQuestionChars) throw new ArgumentException($"The question must be {MaxQuestionChars} characters or fewer.");

        var c = await _db.KnowledgeBenchmarkCases.FirstOrDefaultAsync(x => x.ClinicId == clinicId && x.Id == id, ct);
        if (c is null) return null;

        c.Question = text;
        c.UpdatedAt = DateTimeOffset.UtcNow;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            _db.Entry(c).State = EntityState.Detached;
            throw new ArgumentException("Another case for this chunk already has that question.");
        }

        var last = (await LastResultsAsync(clinicId, new List<Guid> { id }, ct)).GetValueOrDefault(id);
        return ToCaseResponse(c, last);
    }

    public async Task<BenchmarkCaseResponse?> SetReviewedAsync(Guid clinicId, Guid id, bool reviewed, CancellationToken ct = default)
    {
        var c = await _db.KnowledgeBenchmarkCases.FirstOrDefaultAsync(x => x.ClinicId == clinicId && x.Id == id, ct);
        if (c is null) return null;

        c.IsReviewed = reviewed;
        c.ReviewedAt = reviewed ? DateTimeOffset.UtcNow : null;
        c.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        var last = (await LastResultsAsync(clinicId, new List<Guid> { id }, ct)).GetValueOrDefault(id);
        return ToCaseResponse(c, last);
    }

    public async Task<bool> DeleteCaseAsync(Guid clinicId, Guid id, CancellationToken ct = default)
    {
        // Results keep their own snapshot of the question; the FK is ON DELETE SET NULL, so run history survives.
        var deleted = await _db.KnowledgeBenchmarkCases.Where(c => c.ClinicId == clinicId && c.Id == id).ExecuteDeleteAsync(ct);
        return deleted > 0;
    }

    public async Task<IReadOnlyList<BenchmarkSourceDocumentResponse>> ListSourceDocumentsAsync(Guid clinicId, CancellationToken ct = default) =>
        await _db.KnowledgeDocuments.AsNoTracking()
            .Where(d => d.ClinicId == clinicId && d.IsActive && d.Chunks.Any())
            .OrderBy(d => d.Title)
            .Select(d => new BenchmarkSourceDocumentResponse(d.Id, d.Title, d.Category, d.Chunks.Count))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<BenchmarkSourceChunkResponse>?> ListSourceChunksAsync(Guid clinicId, Guid documentId, CancellationToken ct = default)
    {
        var docExists = await _db.KnowledgeDocuments.AsNoTracking().AnyAsync(d => d.ClinicId == clinicId && d.Id == documentId, ct);
        if (!docExists) return null;

        var chunks = await _db.KnowledgeChunks.AsNoTracking()
            .Where(k => k.ClinicId == clinicId && k.KnowledgeDocumentId == documentId)
            .OrderBy(k => k.ChunkIndex).ToListAsync(ct);
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
        if (generationId is { } gid && !await _db.KnowledgeBenchmarkGenerations.AsNoTracking().AnyAsync(g => g.ClinicId == clinicId && g.Id == gid, ct))
        {
            throw new ArgumentException("No such generation for this clinic.");
        }

        await RefreshStaleAsync(clinicId, ct);

        var cutoff = DateTimeOffset.UtcNow - StaleRunAge;
        if (await _db.KnowledgeBenchmarkRuns.AnyAsync(r => r.ClinicId == clinicId && r.CreatedAt > cutoff
                && (r.Status == BenchmarkRunStatus.Pending || r.Status == BenchmarkRunStatus.Running), ct))
        {
            throw new BenchmarkRunInProgressException();
        }

        var inScope = ApplyScope(_db.KnowledgeBenchmarkCases.AsNoTracking().Where(c => c.ClinicId == clinicId), normalizedScope, generationId);
        var total = await inScope.CountAsync(ct);
        var stale = await inScope.CountAsync(c => c.IsStale, ct);
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
        _db.KnowledgeBenchmarkRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        _queue.Enqueue(run.Id);
        return ToSummary(run);
    }

    public async Task ExecuteRunAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await _db.KnowledgeBenchmarkRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null || run.Status != BenchmarkRunStatus.Pending) return;
        var clinicId = run.ClinicId;

        try
        {
            run.Status = BenchmarkRunStatus.Running;
            run.StartedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            // Cases may have gone stale between queueing and starting.
            await RefreshStaleAsync(clinicId, ct);
            var cases = await ApplyScope(_db.KnowledgeBenchmarkCases.AsNoTracking().Where(c => c.ClinicId == clinicId), run.CaseScope, run.GenerationId)
                .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).ToListAsync(ct);

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

                _db.KnowledgeBenchmarkResults.Add(result);
                processed++;
                run.ProcessedCases = processed;
                run.ErrorCases = errorCases;
                run.ScoredCases = scores.Count;
                await _db.SaveChangesAsync(ct);
                _db.Entry(result).State = EntityState.Detached;

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
            await _db.SaveChangesAsync(ct);
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
                _db.ChangeTracker.Clear();
                var failed = await _db.KnowledgeBenchmarkRuns.FirstOrDefaultAsync(r => r.Id == runId, CancellationToken.None);
                if (failed is not null)
                {
                    failed.Status = BenchmarkRunStatus.Failed;
                    failed.CompletedAt = DateTimeOffset.UtcNow;
                    failed.ErrorSummary = Truncate($"The benchmark run failed: {ex.Message}", 500);
                    await _db.SaveChangesAsync(CancellationToken.None);
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
        (await _db.KnowledgeBenchmarkRuns.AsNoTracking()
            .Where(r => r.ClinicId == clinicId)
            .OrderByDescending(r => r.CreatedAt).Take(Math.Clamp(take, 1, 100)).ToListAsync(ct))
        .Select(ToSummary).ToList();

    public async Task<BenchmarkRunSummary?> GetRunAsync(Guid clinicId, Guid id, CancellationToken ct = default)
    {
        var run = await _db.KnowledgeBenchmarkRuns.AsNoTracking().FirstOrDefaultAsync(r => r.ClinicId == clinicId && r.Id == id, ct);
        return run is null ? null : ToSummary(run);
    }

    public async Task<BenchmarkResultListResponse?> ListResultsAsync(Guid clinicId, Guid runId, string? classification, int skip, int take, CancellationToken ct = default)
    {
        if (!await _db.KnowledgeBenchmarkRuns.AsNoTracking().AnyAsync(r => r.ClinicId == clinicId && r.Id == runId, ct)) return null;

        var query = _db.KnowledgeBenchmarkResults.AsNoTracking().Where(r => r.ClinicId == clinicId && r.BenchmarkRunId == runId);
        if (!string.IsNullOrWhiteSpace(classification))
        {
            var wanted = classification.Trim().ToUpperInvariant();
            query = query.Where(r => r.ResultClassification == wanted);
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderBy(r => r.ResultClassification == BenchmarkClassification.Miss ? 0
                : r.ResultClassification == BenchmarkClassification.DocumentOnlyHit ? 1
                : r.ResultClassification == BenchmarkClassification.Error ? 2
                : r.ResultClassification == BenchmarkClassification.StaleCase ? 3 : 4)
            .ThenBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .Skip(Math.Max(skip, 0)).Take(Math.Clamp(take, 1, 500))
            .ToListAsync(ct);

        return new BenchmarkResultListResponse(rows.Select(ToResultResponse).ToList(), total);
    }

    public async Task<BenchmarkResultDetailResponse?> GetResultAsync(Guid clinicId, Guid runId, Guid resultId, CancellationToken ct = default)
    {
        var result = await _db.KnowledgeBenchmarkResults.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ClinicId == clinicId && r.BenchmarkRunId == runId && r.Id == resultId, ct);
        return result is null ? null : await BuildResultDetailAsync(clinicId, result, ct);
    }

    public async Task<IReadOnlyList<Guid>> RecoverInterruptedRunsAsync(CancellationToken ct = default)
    {
        var interrupted = await _db.KnowledgeBenchmarkRuns.Where(r => r.Status == BenchmarkRunStatus.Running).ToListAsync(ct);
        foreach (var run in interrupted)
        {
            run.Status = BenchmarkRunStatus.Failed;
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.ErrorSummary = "The benchmark run was interrupted by an application restart. Run it again.";
        }
        await _db.SaveChangesAsync(ct);

        return await _db.KnowledgeBenchmarkRuns.AsNoTracking()
            .Where(r => r.Status == BenchmarkRunStatus.Pending).OrderBy(r => r.CreatedAt).Select(r => r.Id).ToListAsync(ct);
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
            var cases = await _db.KnowledgeBenchmarkCases.Where(c => c.ClinicId == clinicId).ToListAsync(ct);
            if (cases.Count == 0) return;

            var chunkIds = cases.Select(c => c.ExpectedChunkId).Distinct().ToList();
            var liveChunks = (await _db.KnowledgeChunks.AsNoTracking()
                    .Where(k => k.ClinicId == clinicId && chunkIds.Contains(k.Id))
                    .Select(k => new { k.Id, k.KnowledgeDocumentId, k.Content })
                    .ToListAsync(ct))
                .ToDictionary(k => k.Id);
            var docs = (await _db.KnowledgeDocuments.AsNoTracking()
                    .Where(d => d.ClinicId == clinicId)
                    .Select(d => new { d.Id, d.Title, d.IsActive })
                    .ToListAsync(ct))
                .ToDictionary(d => d.Id);

            Dictionary<string, List<(Guid ChunkId, Guid DocumentId)>>? hashIndex = null;
            async Task<Dictionary<string, List<(Guid ChunkId, Guid DocumentId)>>> HashIndexAsync()
            {
                if (hashIndex is not null) return hashIndex;
                hashIndex = new();
                var active = await _db.KnowledgeChunks.AsNoTracking()
                    .Where(k => k.ClinicId == clinicId && k.Document!.ClinicId == clinicId && k.Document.IsActive)
                    .Select(k => new { k.Id, k.KnowledgeDocumentId, k.Content }).ToListAsync(ct);
                foreach (var k in active)
                {
                    var h = Sha256Hex(k.Content);
                    if (!hashIndex.TryGetValue(h, out var list)) hashIndex[h] = list = new();
                    list.Add((k.Id, k.KnowledgeDocumentId));
                }
                return hashIndex;
            }

            var dirty = false;
            foreach (var c in cases)
            {
                string? reason = null;
                liveChunks.TryGetValue(c.ExpectedChunkId, out var live);
                if (live is null || live.KnowledgeDocumentId != c.ExpectedDocumentId) reason = BenchmarkStaleReason.ChunkMissing;
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

            if (dirty) await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Benchmark stale-case refresh failed for clinic {ClinicId}; continuing with stored flags.", clinicId);
            // Drop only the unsaved case edits — the caller (e.g. a running benchmark) may be tracking other entities.
            foreach (var entry in _db.ChangeTracker.Entries<KnowledgeRetrievalBenchmarkCase>().ToList())
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    private sealed record SampledChunk(Guid ChunkId, Guid DocumentId, string Title, string Content);

    private sealed class SampledChunkRow
    {
        public Guid ChunkId { get; set; }
        public Guid DocumentId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    /// <summary>Picks up to <see cref="GenerationSampleSize"/> ACTIVE chunks of this clinic that have no case yet. Spread
    /// across documents (one random chunk per document first, then a second, …) so one large scraped document can't
    /// dominate, and random, so repeated generations don't keep choosing the same chunks. Chunks whose text already has a
    /// case (e.g. rebuilt under a new id) are skipped too.</summary>
    private async Task<List<SampledChunk>> SampleSourceChunksAsync(Guid clinicId, CancellationToken ct)
    {
        var pool = await _db.Database.SqlQueryRaw<SampledChunkRow>(
            @"select x.""ChunkId"", x.""DocumentId"", x.""Title"", x.""Content"" from (
                select c.id as ""ChunkId"", d.id as ""DocumentId"", d.title as ""Title"", c.content as ""Content"",
                       row_number() over (partition by d.id order by random()) as rn
                from knowledge_chunks c
                join knowledge_documents d on d.id = c.knowledge_document_id
                where c.clinic_id = @clinic and d.clinic_id = @clinic and d.is_active = true
                  and char_length(c.content) >= @minChars
                  and not exists (select 1 from knowledge_retrieval_benchmark_cases b
                                  where b.clinic_id = @clinic and b.expected_chunk_id = c.id)
                  -- Blog post-meta teasers ('482 Views 0 Comments', '... Read More') make poor benchmark questions:
                  -- they are byline/navigation noise, not the article's own answer to anything. This only narrows
                  -- which chunks are OFFERED as test-case sources — it never touches what search actually indexes.
                  and c.content !~* '[0-9]+\s*Views\s+[0-9]+\s*Comments'
                  and c.content not ilike '%Read More%'
              ) x
              order by x.rn, random()
              limit @pool",
            new NpgsqlParameter("clinic", clinicId),
            new NpgsqlParameter("minChars", MinSourceChunkChars),
            new NpgsqlParameter("pool", SamplePoolSize))
            .ToListAsync(ct);

        var coveredHashes = (await _db.KnowledgeBenchmarkCases.AsNoTracking()
                .Where(c => c.ClinicId == clinicId && c.SourceChunkHash != null)
                .Select(c => c.SourceChunkHash!).Distinct().ToListAsync(ct))
            .ToHashSet();

        var picked = new List<SampledChunk>();
        var pickedHashes = new HashSet<string>();
        foreach (var row in pool)
        {
            var hash = Sha256Hex(row.Content);
            if (coveredHashes.Contains(hash) || !pickedHashes.Add(hash)) continue;
            picked.Add(new SampledChunk(row.ChunkId, row.DocumentId, row.Title, row.Content));
            if (picked.Count == GenerationSampleSize) break;
        }
        return picked;
    }

    private static IQueryable<KnowledgeRetrievalBenchmarkCase> ApplyScope(
        IQueryable<KnowledgeRetrievalBenchmarkCase> query, string scope, Guid? generationId = null) => scope switch
    {
        BenchmarkCaseScope.Generated => query.Where(c => c.CaseType == BenchmarkCaseType.Generated),
        BenchmarkCaseScope.Reviewed => query.Where(c => c.IsReviewed),
        // Every case from ONE "Generate Test Cases" request, whatever its type/reviewed state — generationId is
        // already validated as non-null and belonging to this clinic before this is ever called with "generation".
        BenchmarkCaseScope.Generation => query.Where(c => c.GenerationId == generationId),
        _ => query
    };

    private async Task<BenchmarkSettingsSnapshot> CurrentSettingsSnapshotAsync(Guid clinicId, CancellationToken ct)
    {
        var s = await _settings.GetAsync(clinicId, ct);
        var activeChunks = _db.KnowledgeChunks.AsNoTracking().Where(k => k.ClinicId == clinicId && k.Document!.IsActive);
        var count = await activeChunks.CountAsync(ct);
        var avg = count == 0 ? (double?)null : await activeChunks.AverageAsync(k => (double?)k.Content.Length, ct);
        return new BenchmarkSettingsSnapshot(
            s.EmbeddingModel, s.VectorDimension, s.ChunkSizeTokens, s.ChunkOverlapTokens, s.SimilarityMethod,
            s.TopK, s.MinimumSimilarity, count, avg is null ? null : Math.Round(avg.Value, 0));
    }

    private async Task<Dictionary<Guid, BenchmarkCaseLastResult>> LastResultsAsync(Guid clinicId, List<Guid> caseIds, CancellationToken ct)
    {
        if (caseIds.Count == 0) return new();
        var nullableIds = caseIds.Select(i => (Guid?)i).ToList();
        var rows = await _db.KnowledgeBenchmarkResults.AsNoTracking()
            .Where(r => r.ClinicId == clinicId && nullableIds.Contains(r.BenchmarkCaseId))
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new { r.Id, r.BenchmarkRunId, r.BenchmarkCaseId, r.ResultClassification, r.ExpectedChunkRank, r.ExpectedDocumentBestRank, r.CreatedAt })
            .ToListAsync(ct);

        return rows.GroupBy(r => r.BenchmarkCaseId!.Value).ToDictionary(
            g => g.Key,
            g => { var r = g.First(); return new BenchmarkCaseLastResult(r.Id, r.BenchmarkRunId, r.ResultClassification, r.ExpectedChunkRank, r.ExpectedDocumentBestRank, r.CreatedAt); });
    }

    private async Task<string?> LiveChunkContentAsync(Guid clinicId, Guid chunkId, Guid documentId, CancellationToken ct) =>
        await _db.KnowledgeChunks.AsNoTracking()
            .Where(k => k.ClinicId == clinicId && k.Id == chunkId && k.KnowledgeDocumentId == documentId)
            .Select(k => k.Content).FirstOrDefaultAsync(ct);

    private async Task<BenchmarkResultDetailResponse> BuildResultDetailAsync(Guid clinicId, KnowledgeRetrievalBenchmarkResult result, CancellationToken ct)
    {
        var run = await _db.KnowledgeBenchmarkRuns.AsNoTracking().FirstAsync(r => r.ClinicId == clinicId && r.Id == result.BenchmarkRunId, ct);

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
