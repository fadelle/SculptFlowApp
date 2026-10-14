using PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;
using PlasticSurgery.Entities.Requests.KnowledgeBenchmark;
using PlasticSurgery.Entities.Responses.KnowledgeBenchmark;

namespace PlasticSurgery.Business.Contracts.Services.PlatformAdmin;

/// <summary>
/// The Knowledge Retrieval Benchmark for the admin portal, one clinic at a time (the portal picks the clinic). Every call
/// first checks the clinic exists, then delegates to <see cref="Knowledge.IKnowledgeBenchmarkService"/> with that clinic
/// id, so the benchmark's rules live in one place. A clinic, case, generation, run or result that doesn't exist (or isn't
/// the clinic's) throws <see cref="KeyNotFoundException"/>; cancelling a generation that already finished throws
/// <see cref="InvalidOperationException"/>.
/// </summary>
public interface IKnowledgeBenchmarkAdminService
{
    Task<BenchmarkDashboardResponse> GetDashboardAsync(Guid clinicId, CancellationToken ct);

    Task<BenchmarkCaseListResponse> ListCasesAsync(Guid clinicId, BenchmarkCaseFilter filter, int skip, int take, CancellationToken ct);

    Task<BenchmarkCaseDetailResponse> GetCaseAsync(Guid clinicId, Guid id, CancellationToken ct);

    Task<BenchmarkCaseResponse> CreateManualCaseAsync(Guid clinicId, CreateManualBenchmarkCaseRequest request, CancellationToken ct);

    Task<BenchmarkCaseResponse> UpdateCaseQuestionAsync(Guid clinicId, Guid id, string? question, CancellationToken ct);

    Task<BenchmarkCaseResponse> SetReviewedAsync(Guid clinicId, Guid id, bool reviewed, CancellationToken ct);

    Task DeleteCaseAsync(Guid clinicId, Guid id, CancellationToken ct);

    Task<IReadOnlyList<BenchmarkSourceDocumentResponse>> ListSourceDocumentsAsync(Guid clinicId, CancellationToken ct);

    Task<IReadOnlyList<BenchmarkSourceChunkResponse>> ListSourceChunksAsync(Guid clinicId, Guid documentId, CancellationToken ct);

    Task<StartBenchmarkGenerationResponse> StartGenerationAsync(Guid clinicId, CancellationToken ct);

    Task<IReadOnlyList<BenchmarkGenerationSummary>> ListGenerationsAsync(Guid clinicId, int take, CancellationToken ct);

    Task<BenchmarkGenerationDetail> GetGenerationAsync(Guid clinicId, Guid id, CancellationToken ct);

    Task<BenchmarkGenerationSummary> CancelGenerationAsync(Guid clinicId, Guid id, CancellationToken ct);

    Task<BenchmarkRunSummary> StartRunAsync(Guid clinicId, StartBenchmarkRunRequest request, CancellationToken ct);

    Task<IReadOnlyList<BenchmarkRunSummary>> ListRunsAsync(Guid clinicId, int take, CancellationToken ct);

    Task<BenchmarkRunSummary> GetRunAsync(Guid clinicId, Guid id, CancellationToken ct);

    Task<BenchmarkResultListResponse> ListResultsAsync(Guid clinicId, Guid runId, string? classification, int skip, int take, CancellationToken ct);

    Task<IReadOnlyList<BenchmarkRunGenerationBreakdown>> GetRunGenerationBreakdownAsync(Guid clinicId, Guid runId, CancellationToken ct);

    Task<BenchmarkResultDetailResponse> GetResultAsync(Guid clinicId, Guid runId, Guid resultId, CancellationToken ct);
}
