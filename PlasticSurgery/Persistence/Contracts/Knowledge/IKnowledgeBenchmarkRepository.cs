using PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.KnowledgeBenchmark;

namespace PlasticSurgery.Persistence.Contracts.Knowledge;

/// <summary>
/// Retrieval-benchmark data: cases, runs, results, generations, plus the read-only Knowledge Base views the benchmark
/// needs (chunk text, document states, sampling). Entities are tracked unless the method says read-only.
/// </summary>
public interface IKnowledgeBenchmarkRepository
{
    // ---- cases
    Task<BenchmarkCaseCounts> GetCaseCountsAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Read-only page, newest first. <paramref name="view"/>: all, generated, manual, reviewed, unreviewed, stale.</summary>
    Task<(IReadOnlyList<KnowledgeRetrievalBenchmarkCase> Items, int Total)> ListCasesAsync(Guid clinicId, string view, Guid? generationId,
        int skip, int take, CancellationToken ct = default);

    Task<KnowledgeRetrievalBenchmarkCase?> GetCaseReadOnlyAsync(Guid clinicId, Guid caseId, CancellationToken ct = default);

    Task<KnowledgeRetrievalBenchmarkCase?> GetCaseAsync(Guid clinicId, Guid caseId, CancellationToken ct = default);

    /// <summary>Every case of the clinic (stale-flag refresh).</summary>
    Task<IReadOnlyList<KnowledgeRetrievalBenchmarkCase>> ListAllCasesAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Read-only cases in a run scope (all, generated, reviewed, generation), oldest first.</summary>
    Task<IReadOnlyList<KnowledgeRetrievalBenchmarkCase>> ListCasesInScopeAsync(Guid clinicId, string scope, Guid? generationId,
        CancellationToken ct = default);

    Task<(int Total, int Stale)> CountCasesInScopeAsync(Guid clinicId, string scope, Guid? generationId, CancellationToken ct = default);

    void AddCase(KnowledgeRetrievalBenchmarkCase benchmarkCase);

    void AddCases(IEnumerable<KnowledgeRetrievalBenchmarkCase> cases);

    /// <summary>Stops tracking one case (e.g. its insert or update was rejected).</summary>
    void DetachCase(KnowledgeRetrievalBenchmarkCase benchmarkCase);

    /// <summary>Stops tracking every case, keeping other pending changes.</summary>
    void DetachAllCases();

    /// <summary>Deletes directly in the database; results keep their snapshot (FK is ON DELETE SET NULL).</summary>
    Task<int> DeleteCaseAsync(Guid clinicId, Guid caseId, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListCoveredSourceHashesAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>(chunk, question) pairs that already exist for these chunks.</summary>
    Task<IReadOnlyList<(Guid ChunkId, string Question)>> ListQuestionsForChunksAsync(Guid clinicId, IReadOnlyCollection<Guid> chunkIds,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, int>> CountCasesByGenerationAsync(Guid clinicId, IReadOnlyCollection<Guid> generationIds,
        CancellationToken ct = default);

    // ---- Knowledge Base views
    /// <summary>The chunk when it belongs to this clinic and document and its document is active; DocumentTitle set.</summary>
    Task<BenchmarkChunkInfo?> GetActiveChunkAsync(Guid clinicId, Guid documentId, Guid chunkId, CancellationToken ct = default);

    /// <summary>Active documents that have chunks, by title.</summary>
    Task<IReadOnlyList<BenchmarkSourceDocumentResponse>> ListSourceDocumentsAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Read-only, in chunk order.</summary>
    Task<IReadOnlyList<KnowledgeChunk>> ListDocumentChunksAsync(Guid clinicId, Guid documentId, CancellationToken ct = default);

    /// <summary>The clinic's chunks with these ids, whatever their document's state.</summary>
    Task<IReadOnlyList<BenchmarkChunkInfo>> ListChunksAsync(Guid clinicId, IReadOnlyCollection<Guid> chunkIds, CancellationToken ct = default);

    /// <summary>Chunks of the clinic's active documents; all of them when <paramref name="chunkIds"/> is null.</summary>
    Task<IReadOnlyList<BenchmarkChunkInfo>> ListActiveChunksAsync(Guid clinicId, IReadOnlyCollection<Guid>? chunkIds,
        CancellationToken ct = default);

    Task<IReadOnlyList<BenchmarkDocumentState>> ListDocumentStatesAsync(Guid clinicId, CancellationToken ct = default);

    Task<ActiveChunkStats> GetActiveChunkStatsAsync(Guid clinicId, CancellationToken ct = default);

    Task<string?> GetChunkContentAsync(Guid clinicId, Guid chunkId, Guid documentId, CancellationToken ct = default);

    /// <summary>
    /// A random pool of active chunks with no case yet, spread across documents (one per document first, then a second, …),
    /// at least <paramref name="minChars"/> long and without blog teaser noise. DocumentTitle set.
    /// </summary>
    Task<IReadOnlyList<BenchmarkChunkInfo>> SampleUncoveredChunksAsync(Guid clinicId, int minChars, int poolSize, CancellationToken ct = default);

    // ---- runs
    void AddRun(KnowledgeRetrievalBenchmarkRun run);

    Task<KnowledgeRetrievalBenchmarkRun?> GetRunAsync(Guid runId, CancellationToken ct = default);

    Task<KnowledgeRetrievalBenchmarkRun?> GetRunReadOnlyAsync(Guid clinicId, Guid runId, CancellationToken ct = default);

    Task<bool> RunExistsAsync(Guid clinicId, Guid runId, CancellationToken ct = default);

    Task<KnowledgeRetrievalBenchmarkRun?> GetLatestRunAsync(Guid clinicId, bool completedOnly, CancellationToken ct = default);

    /// <summary>Read-only, newest first.</summary>
    Task<IReadOnlyList<KnowledgeRetrievalBenchmarkRun>> ListRunsAsync(Guid clinicId, int take, CancellationToken ct = default);

    /// <summary>A pending or running run created after <paramref name="createdAfter"/>.</summary>
    Task<bool> HasRunInProgressAsync(Guid clinicId, DateTimeOffset createdAfter, CancellationToken ct = default);

    Task<IReadOnlyList<KnowledgeRetrievalBenchmarkRun>> ListRunsInStatusAsync(string status, CancellationToken ct = default);

    /// <summary>Oldest first.</summary>
    Task<IReadOnlyList<Guid>> ListRunIdsInStatusAsync(string status, CancellationToken ct = default);

    // ---- results
    void AddResult(KnowledgeRetrievalBenchmarkResult result);

    void DetachResult(KnowledgeRetrievalBenchmarkResult result);

    Task<KnowledgeRetrievalBenchmarkResult?> GetLatestResultForCaseAsync(Guid clinicId, Guid caseId, CancellationToken ct = default);

    /// <summary>Read-only results of these cases, newest first.</summary>
    Task<IReadOnlyList<KnowledgeRetrievalBenchmarkResult>> ListResultsForCasesAsync(Guid clinicId, IReadOnlyCollection<Guid> caseIds,
        CancellationToken ct = default);

    /// <summary>Misses first, then document-only hits, errors, stale cases, hits.</summary>
    Task<(IReadOnlyList<KnowledgeRetrievalBenchmarkResult> Items, int Total)> ListRunResultsAsync(Guid clinicId, Guid runId,
        string? classification, int skip, int take, CancellationToken ct = default);

    Task<KnowledgeRetrievalBenchmarkResult?> GetResultAsync(Guid clinicId, Guid runId, Guid resultId, CancellationToken ct = default);

    Task<IReadOnlyList<BenchmarkResultRank>> ListRunResultRanksAsync(Guid clinicId, Guid runId, CancellationToken ct = default);

    /// <summary>Results of these generations' cases in COMPLETED runs, with the run time.</summary>
    Task<IReadOnlyList<BenchmarkResultRank>> ListCompletedResultRanksForGenerationsAsync(Guid clinicId,
        IReadOnlyCollection<Guid> generationIds, CancellationToken ct = default);

    // ---- generations
    void AddGeneration(KnowledgeRetrievalBenchmarkGeneration generation);

    void DetachGeneration(KnowledgeRetrievalBenchmarkGeneration generation);

    Task<KnowledgeRetrievalBenchmarkGeneration?> GetGenerationAsync(Guid generationId, CancellationToken ct = default);

    Task<KnowledgeRetrievalBenchmarkGeneration?> GetGenerationReadOnlyAsync(Guid generationId, CancellationToken ct = default);

    Task<KnowledgeRetrievalBenchmarkGeneration?> GetGenerationReadOnlyAsync(Guid clinicId, Guid generationId, CancellationToken ct = default);

    Task<bool> GenerationExistsAsync(Guid clinicId, Guid generationId, CancellationToken ct = default);

    Task<Guid?> GetPendingGenerationIdAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Read-only, newest first.</summary>
    Task<IReadOnlyList<KnowledgeRetrievalBenchmarkGeneration>> ListGenerationsAsync(Guid clinicId, int take, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, DateTimeOffset>> GetGenerationRequestTimesAsync(Guid clinicId, IReadOnlyCollection<Guid> generationIds,
        CancellationToken ct = default);

    /// <summary>Pending → completed in one conditional UPDATE; returns 0 when another delivery claimed it first.</summary>
    Task<int> ClaimPendingGenerationAsync(Guid generationId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Pending → cancelled in one conditional UPDATE; returns 0 when it was no longer pending.</summary>
    Task<int> CancelPendingGenerationAsync(Guid clinicId, Guid generationId, string message, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Pending → failed; never overwrites a generation that already finished.</summary>
    Task FailPendingGenerationAsync(Guid generationId, string error, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Marks the clinic's generations still pending since before <paramref name="cutoff"/> as failed.</summary>
    Task ExpirePendingGenerationsAsync(Guid clinicId, DateTimeOffset cutoff, string message, DateTimeOffset now, CancellationToken ct = default);
}
