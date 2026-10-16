using Omni.App.Entities.Dtos.KnowledgeBenchmark;
using Omni.App.Entities.Requests.KnowledgeBenchmark;
using Omni.App.Entities.Responses.KnowledgeBenchmark;

namespace Omni.App.Business.Contracts.Services.PlatformAdmin;

/// <summary>
/// The Knowledge Retrieval Benchmark for the admin portal, one client at a time (the portal picks the client). Every call
/// first checks the client exists, then delegates to <see cref="Knowledge.IKnowledgeBenchmarkService"/> with that client
/// id, so the benchmark's rules live in one place. A client, case, generation, run or result that doesn't exist (or isn't
/// the client's) throws <see cref="KeyNotFoundException"/>; cancelling a generation that already finished throws
/// <see cref="InvalidOperationException"/>.
/// </summary>
public interface IKnowledgeBenchmarkAdminService
{
    Task<BenchmarkDashboardResponse> GetDashboardAsync(Guid clientId, CancellationToken ct);

    Task<BenchmarkCaseListResponse> ListCasesAsync(Guid clientId, BenchmarkCaseFilter filter, int skip, int take, CancellationToken ct);

    Task<BenchmarkCaseDetailResponse> GetCaseAsync(Guid clientId, Guid id, CancellationToken ct);

    Task<BenchmarkCaseResponse> CreateManualCaseAsync(Guid clientId, CreateManualBenchmarkCaseRequest request, CancellationToken ct);

    Task<BenchmarkCaseResponse> UpdateCaseQuestionAsync(Guid clientId, Guid id, string? question, CancellationToken ct);

    Task<BenchmarkCaseResponse> SetReviewedAsync(Guid clientId, Guid id, bool reviewed, CancellationToken ct);

    Task DeleteCaseAsync(Guid clientId, Guid id, CancellationToken ct);

    Task<IReadOnlyList<BenchmarkSourceDocumentResponse>> ListSourceDocumentsAsync(Guid clientId, CancellationToken ct);

    Task<IReadOnlyList<BenchmarkSourceChunkResponse>> ListSourceChunksAsync(Guid clientId, Guid documentId, CancellationToken ct);

    Task<StartBenchmarkGenerationResponse> StartGenerationAsync(Guid clientId, CancellationToken ct);

    Task<IReadOnlyList<BenchmarkGenerationSummary>> ListGenerationsAsync(Guid clientId, int take, CancellationToken ct);

    Task<BenchmarkGenerationDetail> GetGenerationAsync(Guid clientId, Guid id, CancellationToken ct);

    Task<BenchmarkGenerationSummary> CancelGenerationAsync(Guid clientId, Guid id, CancellationToken ct);

    Task<BenchmarkRunSummary> StartRunAsync(Guid clientId, StartBenchmarkRunRequest request, CancellationToken ct);

    Task<IReadOnlyList<BenchmarkRunSummary>> ListRunsAsync(Guid clientId, int take, CancellationToken ct);

    Task<BenchmarkRunSummary> GetRunAsync(Guid clientId, Guid id, CancellationToken ct);

    Task<BenchmarkResultListResponse> ListResultsAsync(Guid clientId, Guid runId, string? classification, int skip, int take, CancellationToken ct);

    Task<IReadOnlyList<BenchmarkRunGenerationBreakdown>> GetRunGenerationBreakdownAsync(Guid clientId, Guid runId, CancellationToken ct);

    Task<BenchmarkResultDetailResponse> GetResultAsync(Guid clientId, Guid runId, Guid resultId, CancellationToken ct);
}
