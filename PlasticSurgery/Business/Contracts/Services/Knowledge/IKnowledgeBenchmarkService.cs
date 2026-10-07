using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;
using PlasticSurgery.Entities.Requests.KnowledgeBenchmark;
using PlasticSurgery.Entities.Responses.KnowledgeBenchmark;

namespace PlasticSurgery.Business.Contracts.Services.Knowledge;

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
