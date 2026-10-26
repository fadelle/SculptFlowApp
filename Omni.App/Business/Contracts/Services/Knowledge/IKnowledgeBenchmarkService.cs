using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Omni.App.Entities.Dtos.KnowledgeBenchmark;
using Omni.App.Entities.Requests.KnowledgeBenchmark;
using Omni.App.Entities.Responses.KnowledgeBenchmark;

namespace Omni.App.Business.Contracts.Services.Knowledge;

/// <summary>
/// Orchestrates the Knowledge Retrieval Benchmark: sampling source chunks, validating generated questions, storing
/// cases, detecting stale ones, executing runs and reading results back. It is a CLIENT of the production retrieval
/// engine (<see cref="IKnowledgeSearchService"/>) — it never owns or duplicates retrieval — and nothing in the
/// production Knowledge Base depends on it. Every method is scoped to a client id: the client the admin portal picked
/// (via <see cref="PlatformAdmin.IKnowledgeBenchmarkAdminService"/>, which checks it exists), or, for
/// <see cref="ExecuteRunAsync"/> and the n8n callback, the client stored on the run/generation row.
/// </summary>
public interface IKnowledgeBenchmarkService
{
    Task<BenchmarkDashboardResponse> GetDashboardAsync(Guid clientId, CancellationToken ct = default);

    Task<BenchmarkCaseListResponse> ListCasesAsync(Guid clientId, BenchmarkCaseFilter filter, int skip, int take, CancellationToken ct = default);
    Task<BenchmarkCaseDetailResponse?> GetCaseAsync(Guid clientId, Guid id, CancellationToken ct = default);

    /// <summary>Samples up to 20 active chunks (not already covered by a case) from THIS client, records a pending generation and
    /// hands the chunks to the n8n generator, then returns immediately (202). n8n calls back with the questions —
    /// see <see cref="ReceiveGenerationResultAsync"/>. A workflow that answers synchronously is processed right away.</summary>
    Task<StartBenchmarkGenerationResponse> StartGenerationAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Applies the questions n8n sent for a generation (its callback, or a synchronous reply). The client and the set of
    /// chunks that were sent come from the STORED generation — never from the caller — and every returned id is validated against
    /// them and the live Knowledge Base. Idempotent: a second delivery is reported as already processed.</summary>
    Task<GenerationReceiveResult> ReceiveGenerationResultAsync(Guid generationId, GeneratorResponse reply, string? rawBody, bool requireEcho, CancellationToken ct = default);

    /// <summary>Marks a still-pending generation failed with <paramref name="message"/> (shown on the portal), as when the question
    /// generator can't be reached. Never overwrites a finished one; never throws.</summary>
    Task FailGenerationAsync(Guid generationId, string message, CancellationToken ct = default);

    /// <summary>"Stop generating": marks THIS client's pending generation cancelled so Generate is free again and n8n's late reply is
    /// refused. It cannot halt n8n's own run. A generation that already finished is reported as such, not changed.</summary>
    Task<GenerationCancelResult> CancelGenerationAsync(Guid clientId, Guid generationId, CancellationToken ct = default);

    Task<IReadOnlyList<BenchmarkGenerationSummary>> ListGenerationsAsync(Guid clientId, int take, CancellationToken ct = default);
    Task<BenchmarkGenerationDetail?> GetGenerationAsync(Guid clientId, Guid id, CancellationToken ct = default);
    /// <summary>Scores of a run split by the generation each case came from. Null when the run isn't this client's.</summary>
    Task<IReadOnlyList<BenchmarkRunGenerationBreakdown>?> GetRunGenerationBreakdownAsync(Guid clientId, Guid runId, CancellationToken ct = default);

    Task<BenchmarkCaseResponse> CreateManualCaseAsync(Guid clientId, CreateManualBenchmarkCaseRequest request, CancellationToken ct = default);
    Task<BenchmarkCaseResponse?> UpdateCaseQuestionAsync(Guid clientId, Guid id, string? question, CancellationToken ct = default);
    Task<BenchmarkCaseResponse?> SetReviewedAsync(Guid clientId, Guid id, bool reviewed, CancellationToken ct = default);
    Task<bool> DeleteCaseAsync(Guid clientId, Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<BenchmarkSourceDocumentResponse>> ListSourceDocumentsAsync(Guid clientId, CancellationToken ct = default);
    /// <summary>Null when the document isn't one of this client's.</summary>
    Task<IReadOnlyList<BenchmarkSourceChunkResponse>?> ListSourceChunksAsync(Guid clientId, Guid documentId, CancellationToken ct = default);

    /// <summary>Creates a pending run for the scope ("all" | "generated" | "reviewed" | "generation") and queues it for
    /// the background worker. "generation" requires <paramref name="generationId"/> (a generation of THIS client) and
    /// scores only that batch's cases, whatever their type/reviewed state. Throws
    /// <see cref="BenchmarkRunInProgressException"/> if one is already active.</summary>
    Task<BenchmarkRunSummary> StartRunAsync(Guid clientId, string? scope, Guid? generationId = null, CancellationToken ct = default);

    /// <summary>Executes a queued run (called by the background worker). Never throws for a benchmark failure — it
    /// records it on the run instead.</summary>
    Task ExecuteRunAsync(Guid runId, CancellationToken ct = default);

    Task<IReadOnlyList<BenchmarkRunSummary>> ListRunsAsync(Guid clientId, int take, CancellationToken ct = default);
    Task<BenchmarkRunSummary?> GetRunAsync(Guid clientId, Guid id, CancellationToken ct = default);
    /// <summary>Null when the run isn't one of this client's.</summary>
    Task<BenchmarkResultListResponse?> ListResultsAsync(Guid clientId, Guid runId, string? classification, int skip, int take, CancellationToken ct = default);
    Task<BenchmarkResultDetailResponse?> GetResultAsync(Guid clientId, Guid runId, Guid resultId, CancellationToken ct = default);

    /// <summary>Startup recovery: marks runs a previous process left running as failed and returns the pending run ids
    /// to queue again. Maintenance only — not user-facing, so not client-scoped.</summary>
    Task<IReadOnlyList<Guid>> RecoverInterruptedRunsAsync(CancellationToken ct = default);
}
