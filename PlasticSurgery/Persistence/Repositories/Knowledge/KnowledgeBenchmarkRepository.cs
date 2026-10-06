using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.KnowledgeBenchmark;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Knowledge;

namespace PlasticSurgery.Persistence.Repositories.Knowledge;

public class KnowledgeBenchmarkRepository : IKnowledgeBenchmarkRepository
{
    private readonly ApplicationDbContext _db;

    public KnowledgeBenchmarkRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    // ------------------------------------------------------------------ cases

    public async Task<BenchmarkCaseCounts> GetCaseCountsAsync(Guid clinicId, CancellationToken ct = default)
    {
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
        return counts is null
            ? new BenchmarkCaseCounts(0, 0, 0, 0, 0)
            : new BenchmarkCaseCounts(counts.Total, counts.Generated, counts.Manual, counts.Reviewed, counts.Stale);
    }

    public async Task<(IReadOnlyList<KnowledgeRetrievalBenchmarkCase> Items, int Total)> ListCasesAsync(Guid clinicId, string view,
        Guid? generationId, int skip, int take, CancellationToken ct = default)
    {
        var query = _db.KnowledgeBenchmarkCases.AsNoTracking().Where(c => c.ClinicId == clinicId);
        query = view switch
        {
            "generated" => query.Where(c => c.CaseType == BenchmarkCaseType.Generated),
            "manual" => query.Where(c => c.CaseType == BenchmarkCaseType.Manual),
            "reviewed" => query.Where(c => c.IsReviewed),
            "unreviewed" => query.Where(c => !c.IsReviewed),
            "stale" => query.Where(c => c.IsStale),
            _ => query
        };
        if (generationId is { } generationFilter) query = query.Where(c => c.GenerationId == generationFilter);

        var total = await query.CountAsync(ct);
        var page = await query.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id).Skip(skip).Take(take).ToListAsync(ct);
        return (page, total);
    }

    public Task<KnowledgeRetrievalBenchmarkCase?> GetCaseReadOnlyAsync(Guid clinicId, Guid caseId, CancellationToken ct = default) =>
        _db.KnowledgeBenchmarkCases.AsNoTracking().FirstOrDefaultAsync(x => x.ClinicId == clinicId && x.Id == caseId, ct);

    public Task<KnowledgeRetrievalBenchmarkCase?> GetCaseAsync(Guid clinicId, Guid caseId, CancellationToken ct = default) =>
        _db.KnowledgeBenchmarkCases.FirstOrDefaultAsync(x => x.ClinicId == clinicId && x.Id == caseId, ct);

    public async Task<IReadOnlyList<KnowledgeRetrievalBenchmarkCase>> ListAllCasesAsync(Guid clinicId, CancellationToken ct = default) =>
        await _db.KnowledgeBenchmarkCases.Where(c => c.ClinicId == clinicId).ToListAsync(ct);

    public async Task<IReadOnlyList<KnowledgeRetrievalBenchmarkCase>> ListCasesInScopeAsync(Guid clinicId, string scope, Guid? generationId,
        CancellationToken ct = default) =>
        await InScope(clinicId, scope, generationId).OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).ToListAsync(ct);

    public async Task<(int Total, int Stale)> CountCasesInScopeAsync(Guid clinicId, string scope, Guid? generationId,
        CancellationToken ct = default)
    {
        var inScope = InScope(clinicId, scope, generationId);
        var total = await inScope.CountAsync(ct);
        var stale = await inScope.CountAsync(c => c.IsStale, ct);
        return (total, stale);
    }

    private IQueryable<KnowledgeRetrievalBenchmarkCase> InScope(Guid clinicId, string scope, Guid? generationId)
    {
        var query = _db.KnowledgeBenchmarkCases.AsNoTracking().Where(c => c.ClinicId == clinicId);
        return scope switch
        {
            BenchmarkCaseScope.Generated => query.Where(c => c.CaseType == BenchmarkCaseType.Generated),
            BenchmarkCaseScope.Reviewed => query.Where(c => c.IsReviewed),
            // Every case from ONE "Generate Test Cases" request, whatever its type/reviewed state.
            BenchmarkCaseScope.Generation => query.Where(c => c.GenerationId == generationId),
            _ => query
        };
    }

    public void AddCase(KnowledgeRetrievalBenchmarkCase benchmarkCase) => _db.KnowledgeBenchmarkCases.Add(benchmarkCase);

    public void AddCases(IEnumerable<KnowledgeRetrievalBenchmarkCase> cases) => _db.KnowledgeBenchmarkCases.AddRange(cases);

    public void DetachCase(KnowledgeRetrievalBenchmarkCase benchmarkCase) => _db.Entry(benchmarkCase).State = EntityState.Detached;

    public void DetachAllCases()
    {
        foreach (var entry in _db.ChangeTracker.Entries<KnowledgeRetrievalBenchmarkCase>().ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    public Task<int> DeleteCaseAsync(Guid clinicId, Guid caseId, CancellationToken ct = default) =>
        _db.KnowledgeBenchmarkCases.Where(c => c.ClinicId == clinicId && c.Id == caseId).ExecuteDeleteAsync(ct);

    public async Task<IReadOnlyList<string>> ListCoveredSourceHashesAsync(Guid clinicId, CancellationToken ct = default) =>
        await _db.KnowledgeBenchmarkCases.AsNoTracking()
            .Where(c => c.ClinicId == clinicId && c.SourceChunkHash != null)
            .Select(c => c.SourceChunkHash!).Distinct().ToListAsync(ct);

    public async Task<IReadOnlyList<(Guid ChunkId, string Question)>> ListQuestionsForChunksAsync(Guid clinicId,
        IReadOnlyCollection<Guid> chunkIds, CancellationToken ct = default)
    {
        var rows = await _db.KnowledgeBenchmarkCases.AsNoTracking()
            .Where(c => c.ClinicId == clinicId && chunkIds.Contains(c.ExpectedChunkId))
            .Select(c => new { c.ExpectedChunkId, c.Question }).ToListAsync(ct);
        return rows.Select(r => (r.ExpectedChunkId, r.Question)).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountCasesByGenerationAsync(Guid clinicId, IReadOnlyCollection<Guid> generationIds,
        CancellationToken ct = default)
    {
        var nullableIds = generationIds.Select(i => (Guid?)i).ToList();
        var rows = await _db.KnowledgeBenchmarkCases.AsNoTracking()
            .Where(c => c.ClinicId == clinicId && nullableIds.Contains(c.GenerationId))
            .GroupBy(c => c.GenerationId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return rows.Where(x => x.Key != null).ToDictionary(x => x.Key!.Value, x => x.Count);
    }

    // ------------------------------------------------------------------ Knowledge Base views

    public Task<BenchmarkChunkInfo?> GetActiveChunkAsync(Guid clinicId, Guid documentId, Guid chunkId, CancellationToken ct = default) =>
        _db.KnowledgeChunks.AsNoTracking()
            .Where(k => k.ClinicId == clinicId && k.Id == chunkId && k.KnowledgeDocumentId == documentId
                        && k.Document!.ClinicId == clinicId && k.Document.IsActive)
            .Select(k => new BenchmarkChunkInfo(k.Id, k.KnowledgeDocumentId, k.Content, k.Document!.Title))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<BenchmarkSourceDocumentResponse>> ListSourceDocumentsAsync(Guid clinicId, CancellationToken ct = default) =>
        await _db.KnowledgeDocuments.AsNoTracking()
            .Where(d => d.ClinicId == clinicId && d.IsActive && d.Chunks.Any())
            .OrderBy(d => d.Title)
            .Select(d => new BenchmarkSourceDocumentResponse(d.Id, d.Title, d.Category, d.Chunks.Count))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<KnowledgeChunk>> ListDocumentChunksAsync(Guid clinicId, Guid documentId, CancellationToken ct = default) =>
        await _db.KnowledgeChunks.AsNoTracking()
            .Where(k => k.ClinicId == clinicId && k.KnowledgeDocumentId == documentId)
            .OrderBy(k => k.ChunkIndex).ToListAsync(ct);

    public async Task<IReadOnlyList<BenchmarkChunkInfo>> ListChunksAsync(Guid clinicId, IReadOnlyCollection<Guid> chunkIds,
        CancellationToken ct = default) =>
        await _db.KnowledgeChunks.AsNoTracking()
            .Where(k => k.ClinicId == clinicId && chunkIds.Contains(k.Id))
            .Select(k => new BenchmarkChunkInfo(k.Id, k.KnowledgeDocumentId, k.Content, null))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<BenchmarkChunkInfo>> ListActiveChunksAsync(Guid clinicId, IReadOnlyCollection<Guid>? chunkIds,
        CancellationToken ct = default)
    {
        var query = _db.KnowledgeChunks.AsNoTracking()
            .Where(k => k.ClinicId == clinicId && k.Document!.ClinicId == clinicId && k.Document.IsActive);
        if (chunkIds is not null) query = query.Where(k => chunkIds.Contains(k.Id));
        return await query.Select(k => new BenchmarkChunkInfo(k.Id, k.KnowledgeDocumentId, k.Content, null)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<BenchmarkDocumentState>> ListDocumentStatesAsync(Guid clinicId, CancellationToken ct = default) =>
        await _db.KnowledgeDocuments.AsNoTracking()
            .Where(d => d.ClinicId == clinicId)
            .Select(d => new BenchmarkDocumentState(d.Id, d.Title, d.IsActive))
            .ToListAsync(ct);

    public async Task<ActiveChunkStats> GetActiveChunkStatsAsync(Guid clinicId, CancellationToken ct = default)
    {
        var activeChunks = _db.KnowledgeChunks.AsNoTracking().Where(k => k.ClinicId == clinicId && k.Document!.IsActive);
        var count = await activeChunks.CountAsync(ct);
        var avg = count == 0 ? (double?)null : await activeChunks.AverageAsync(k => (double?)k.Content.Length, ct);
        return new ActiveChunkStats(count, avg);
    }

    public Task<string?> GetChunkContentAsync(Guid clinicId, Guid chunkId, Guid documentId, CancellationToken ct = default) =>
        _db.KnowledgeChunks.AsNoTracking()
            .Where(k => k.ClinicId == clinicId && k.Id == chunkId && k.KnowledgeDocumentId == documentId)
            .Select(k => k.Content).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<BenchmarkChunkInfo>> SampleUncoveredChunksAsync(Guid clinicId, int minChars, int poolSize,
        CancellationToken ct = default)
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
            new NpgsqlParameter("minChars", minChars),
            new NpgsqlParameter("pool", poolSize))
            .ToListAsync(ct);

        return pool.Select(r => new BenchmarkChunkInfo(r.ChunkId, r.DocumentId, r.Content, r.Title)).ToList();
    }

    private sealed class SampledChunkRow
    {
        public Guid ChunkId { get; set; }
        public Guid DocumentId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    // ------------------------------------------------------------------ runs

    public void AddRun(KnowledgeRetrievalBenchmarkRun run) => _db.KnowledgeBenchmarkRuns.Add(run);

    public Task<KnowledgeRetrievalBenchmarkRun?> GetRunAsync(Guid runId, CancellationToken ct = default) =>
        _db.KnowledgeBenchmarkRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);

    public Task<KnowledgeRetrievalBenchmarkRun?> GetRunReadOnlyAsync(Guid clinicId, Guid runId, CancellationToken ct = default) =>
        _db.KnowledgeBenchmarkRuns.AsNoTracking().FirstOrDefaultAsync(r => r.ClinicId == clinicId && r.Id == runId, ct);

    public Task<bool> RunExistsAsync(Guid clinicId, Guid runId, CancellationToken ct = default) =>
        _db.KnowledgeBenchmarkRuns.AsNoTracking().AnyAsync(r => r.ClinicId == clinicId && r.Id == runId, ct);

    public Task<KnowledgeRetrievalBenchmarkRun?> GetLatestRunAsync(Guid clinicId, bool completedOnly, CancellationToken ct = default) =>
        _db.KnowledgeBenchmarkRuns.AsNoTracking()
            .Where(r => r.ClinicId == clinicId && (!completedOnly || r.Status == BenchmarkRunStatus.Completed))
            .OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<KnowledgeRetrievalBenchmarkRun>> ListRunsAsync(Guid clinicId, int take, CancellationToken ct = default) =>
        await _db.KnowledgeBenchmarkRuns.AsNoTracking()
            .Where(r => r.ClinicId == clinicId)
            .OrderByDescending(r => r.CreatedAt).Take(take).ToListAsync(ct);

    public Task<bool> HasRunInProgressAsync(Guid clinicId, DateTimeOffset createdAfter, CancellationToken ct = default) =>
        _db.KnowledgeBenchmarkRuns.AsNoTracking().AnyAsync(r =>
            r.ClinicId == clinicId && r.CreatedAt > createdAfter
            && (r.Status == BenchmarkRunStatus.Pending || r.Status == BenchmarkRunStatus.Running), ct);

    public async Task<IReadOnlyList<KnowledgeRetrievalBenchmarkRun>> ListRunsInStatusAsync(string status, CancellationToken ct = default) =>
        await _db.KnowledgeBenchmarkRuns.Where(r => r.Status == status).ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> ListRunIdsInStatusAsync(string status, CancellationToken ct = default) =>
        await _db.KnowledgeBenchmarkRuns.AsNoTracking()
            .Where(r => r.Status == status).OrderBy(r => r.CreatedAt).Select(r => r.Id).ToListAsync(ct);

    // ------------------------------------------------------------------ results

    public void AddResult(KnowledgeRetrievalBenchmarkResult result) => _db.KnowledgeBenchmarkResults.Add(result);

    public void DetachResult(KnowledgeRetrievalBenchmarkResult result) => _db.Entry(result).State = EntityState.Detached;

    public Task<KnowledgeRetrievalBenchmarkResult?> GetLatestResultForCaseAsync(Guid clinicId, Guid caseId, CancellationToken ct = default) =>
        _db.KnowledgeBenchmarkResults.AsNoTracking()
            .Where(r => r.ClinicId == clinicId && r.BenchmarkCaseId == caseId)
            .OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<KnowledgeRetrievalBenchmarkResult>> ListResultsForCasesAsync(Guid clinicId,
        IReadOnlyCollection<Guid> caseIds, CancellationToken ct = default)
    {
        var nullableIds = caseIds.Select(i => (Guid?)i).ToList();
        return await _db.KnowledgeBenchmarkResults.AsNoTracking()
            .Where(r => r.ClinicId == clinicId && nullableIds.Contains(r.BenchmarkCaseId))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<KnowledgeRetrievalBenchmarkResult> Items, int Total)> ListRunResultsAsync(Guid clinicId, Guid runId,
        string? classification, int skip, int take, CancellationToken ct = default)
    {
        var query = _db.KnowledgeBenchmarkResults.AsNoTracking().Where(r => r.ClinicId == clinicId && r.BenchmarkRunId == runId);
        if (!string.IsNullOrWhiteSpace(classification)) query = query.Where(r => r.ResultClassification == classification);

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderBy(r => r.ResultClassification == BenchmarkClassification.Miss ? 0
                : r.ResultClassification == BenchmarkClassification.DocumentOnlyHit ? 1
                : r.ResultClassification == BenchmarkClassification.Error ? 2
                : r.ResultClassification == BenchmarkClassification.StaleCase ? 3 : 4)
            .ThenBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .Skip(skip).Take(take)
            .ToListAsync(ct);
        return (rows, total);
    }

    public Task<KnowledgeRetrievalBenchmarkResult?> GetResultAsync(Guid clinicId, Guid runId, Guid resultId, CancellationToken ct = default) =>
        _db.KnowledgeBenchmarkResults.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ClinicId == clinicId && r.BenchmarkRunId == runId && r.Id == resultId, ct);

    public async Task<IReadOnlyList<BenchmarkResultRank>> ListRunResultRanksAsync(Guid clinicId, Guid runId, CancellationToken ct = default) =>
        await _db.KnowledgeBenchmarkResults.AsNoTracking()
            .Where(r => r.ClinicId == clinicId && r.BenchmarkRunId == runId)
            .Select(r => new BenchmarkResultRank(r.GenerationId, r.BenchmarkRunId, null, r.ResultClassification, r.ExpectedChunkRank,
                r.ExpectedDocumentBestRank))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<BenchmarkResultRank>> ListCompletedResultRanksForGenerationsAsync(Guid clinicId,
        IReadOnlyCollection<Guid> generationIds, CancellationToken ct = default)
    {
        var nullableIds = generationIds.Select(i => (Guid?)i).ToList();
        return await (
            from r in _db.KnowledgeBenchmarkResults.AsNoTracking()
            join run in _db.KnowledgeBenchmarkRuns.AsNoTracking() on r.BenchmarkRunId equals run.Id
            where r.ClinicId == clinicId && run.ClinicId == clinicId && run.Status == BenchmarkRunStatus.Completed
                  && nullableIds.Contains(r.GenerationId)
            select new BenchmarkResultRank(r.GenerationId, r.BenchmarkRunId, run.CreatedAt, r.ResultClassification,
                r.ExpectedChunkRank, r.ExpectedDocumentBestRank))
            .ToListAsync(ct);
    }

    // ------------------------------------------------------------------ generations

    public void AddGeneration(KnowledgeRetrievalBenchmarkGeneration generation) => _db.KnowledgeBenchmarkGenerations.Add(generation);

    public void DetachGeneration(KnowledgeRetrievalBenchmarkGeneration generation) => _db.Entry(generation).State = EntityState.Detached;

    public Task<KnowledgeRetrievalBenchmarkGeneration?> GetGenerationAsync(Guid generationId, CancellationToken ct = default) =>
        _db.KnowledgeBenchmarkGenerations.FirstOrDefaultAsync(g => g.Id == generationId, ct);

    public Task<KnowledgeRetrievalBenchmarkGeneration?> GetGenerationReadOnlyAsync(Guid generationId, CancellationToken ct = default) =>
        _db.KnowledgeBenchmarkGenerations.AsNoTracking().FirstOrDefaultAsync(g => g.Id == generationId, ct);

    public Task<KnowledgeRetrievalBenchmarkGeneration?> GetGenerationReadOnlyAsync(Guid clinicId, Guid generationId,
        CancellationToken ct = default) =>
        _db.KnowledgeBenchmarkGenerations.AsNoTracking().FirstOrDefaultAsync(g => g.ClinicId == clinicId && g.Id == generationId, ct);

    public Task<bool> GenerationExistsAsync(Guid clinicId, Guid generationId, CancellationToken ct = default) =>
        _db.KnowledgeBenchmarkGenerations.AsNoTracking().AnyAsync(g => g.ClinicId == clinicId && g.Id == generationId, ct);

    public Task<Guid?> GetPendingGenerationIdAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.KnowledgeBenchmarkGenerations.AsNoTracking()
            .Where(g => g.ClinicId == clinicId && g.Status == BenchmarkGenerationStatus.Pending)
            .OrderByDescending(g => g.CreatedAt).Select(g => (Guid?)g.Id).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<KnowledgeRetrievalBenchmarkGeneration>> ListGenerationsAsync(Guid clinicId, int take,
        CancellationToken ct = default) =>
        await _db.KnowledgeBenchmarkGenerations.AsNoTracking()
            .Where(g => g.ClinicId == clinicId)
            .OrderByDescending(g => g.CreatedAt).Take(take).ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, DateTimeOffset>> GetGenerationRequestTimesAsync(Guid clinicId,
        IReadOnlyCollection<Guid> generationIds, CancellationToken ct = default) =>
        await _db.KnowledgeBenchmarkGenerations.AsNoTracking()
            .Where(g => g.ClinicId == clinicId && generationIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.CreatedAt, ct);

    public Task<int> ClaimPendingGenerationAsync(Guid generationId, DateTimeOffset now, CancellationToken ct = default) =>
        _db.KnowledgeBenchmarkGenerations
            .Where(g => g.Id == generationId && g.Status == BenchmarkGenerationStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.Status, BenchmarkGenerationStatus.Completed).SetProperty(g => g.CompletedAt, now), ct);

    public Task<int> CancelPendingGenerationAsync(Guid clinicId, Guid generationId, string message, DateTimeOffset now,
        CancellationToken ct = default) =>
        _db.KnowledgeBenchmarkGenerations
            .Where(g => g.ClinicId == clinicId && g.Id == generationId && g.Status == BenchmarkGenerationStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(g => g.Status, BenchmarkGenerationStatus.Cancelled)
                .SetProperty(g => g.ErrorMessage, message)
                .SetProperty(g => g.CompletedAt, now), ct);

    public Task FailPendingGenerationAsync(Guid generationId, string error, DateTimeOffset now, CancellationToken ct = default) =>
        _db.KnowledgeBenchmarkGenerations
            .Where(g => g.Id == generationId && g.Status == BenchmarkGenerationStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(g => g.Status, BenchmarkGenerationStatus.Failed)
                .SetProperty(g => g.ErrorMessage, error)
                .SetProperty(g => g.CompletedAt, now), ct);

    public Task ExpirePendingGenerationsAsync(Guid clinicId, DateTimeOffset cutoff, string message, DateTimeOffset now,
        CancellationToken ct = default) =>
        _db.KnowledgeBenchmarkGenerations
            .Where(g => g.ClinicId == clinicId && g.Status == BenchmarkGenerationStatus.Pending && g.CreatedAt < cutoff)
            .ExecuteUpdateAsync(s => s
                .SetProperty(g => g.Status, BenchmarkGenerationStatus.Failed)
                .SetProperty(g => g.ErrorMessage, message)
                .SetProperty(g => g.CompletedAt, now), ct);
}
