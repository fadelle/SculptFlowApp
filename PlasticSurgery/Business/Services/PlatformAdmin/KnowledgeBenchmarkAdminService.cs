using PlasticSurgery.Business.Contracts.Services.Knowledge;
using PlasticSurgery.Business.Contracts.Services.PlatformAdmin;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;
using PlasticSurgery.Entities.Requests.KnowledgeBenchmark;
using PlasticSurgery.Entities.Responses.KnowledgeBenchmark;
using PlasticSurgery.Persistence.Contracts.Clinics;

namespace PlasticSurgery.Business.Services.PlatformAdmin;

/// <summary>Checks the clinic, then hands every call to the benchmark service the clinic dashboard used to call, so the
/// benchmark's rules (sampling, validation, stale detection, one run/generation at a time) stay in one place. "Not this
/// clinic's" answers from that service become <see cref="KeyNotFoundException"/>.</summary>
public class KnowledgeBenchmarkAdminService : IKnowledgeBenchmarkAdminService
{
    private readonly IKnowledgeBenchmarkService _benchmark;
    private readonly IClinicRepository _clinics;

    public KnowledgeBenchmarkAdminService(IKnowledgeBenchmarkService benchmark, IClinicRepository clinics)
    {
        _benchmark = benchmark;
        _clinics = clinics;
    }

    public async Task<BenchmarkDashboardResponse> GetDashboardAsync(Guid clinicId, CancellationToken ct)
    {
        await EnsureClinicAsync(clinicId, ct);
        return await _benchmark.GetDashboardAsync(clinicId, ct);
    }

    public async Task<BenchmarkCaseListResponse> ListCasesAsync(Guid clinicId, BenchmarkCaseFilter filter, int skip, int take, CancellationToken ct)
    {
        await EnsureClinicAsync(clinicId, ct);
        return await _benchmark.ListCasesAsync(clinicId, filter, skip, take, ct);
    }

    public async Task<BenchmarkCaseDetailResponse> GetCaseAsync(Guid clinicId, Guid id, CancellationToken ct)
    {
        await EnsureClinicAsync(clinicId, ct);
        return Found(await _benchmark.GetCaseAsync(clinicId, id, ct), "Test case");
    }

    public async Task<BenchmarkCaseResponse> CreateManualCaseAsync(Guid clinicId, CreateManualBenchmarkCaseRequest request, CancellationToken ct)
    {
        await EnsureClinicAsync(clinicId, ct);
        return await _benchmark.CreateManualCaseAsync(clinicId, request, ct);
    }

    public async Task<BenchmarkCaseResponse> UpdateCaseQuestionAsync(Guid clinicId, Guid id, string? question, CancellationToken ct)
    {
        await EnsureClinicAsync(clinicId, ct);
        return Found(await _benchmark.UpdateCaseQuestionAsync(clinicId, id, question, ct), "Test case");
    }

    public async Task<BenchmarkCaseResponse> SetReviewedAsync(Guid clinicId, Guid id, bool reviewed, CancellationToken ct)
    {
        await EnsureClinicAsync(clinicId, ct);
        return Found(await _benchmark.SetReviewedAsync(clinicId, id, reviewed, ct), "Test case");
    }

    public async Task DeleteCaseAsync(Guid clinicId, Guid id, CancellationToken ct)
    {
        await EnsureClinicAsync(clinicId, ct);
        if (!await _benchmark.DeleteCaseAsync(clinicId, id, ct)) throw new KeyNotFoundException("Test case not found.");
    }

    public async Task<IReadOnlyList<BenchmarkSourceDocumentResponse>> ListSourceDocumentsAsync(Guid clinicId, CancellationToken ct)
    {
        await EnsureClinicAsync(clinicId, ct);
        return await _benchmark.ListSourceDocumentsAsync(clinicId, ct);
    }

    public async Task<IReadOnlyList<BenchmarkSourceChunkResponse>> ListSourceChunksAsync(Guid clinicId, Guid documentId, CancellationToken ct)
    {
        await EnsureClinicAsync(clinicId, ct);
        return Found(await _benchmark.ListSourceChunksAsync(clinicId, documentId, ct), "Document");
    }

    public async Task<StartBenchmarkGenerationResponse> StartGenerationAsync(Guid clinicId, CancellationToken ct)
    {
        await EnsureClinicAsync(clinicId, ct);
        return await _benchmark.StartGenerationAsync(clinicId, ct);
    }

    public async Task<IReadOnlyList<BenchmarkGenerationSummary>> ListGenerationsAsync(Guid clinicId, int take, CancellationToken ct)
    {
        await EnsureClinicAsync(clinicId, ct);
        return await _benchmark.ListGenerationsAsync(clinicId, take, ct);
    }

    public async Task<BenchmarkGenerationDetail> GetGenerationAsync(Guid clinicId, Guid id, CancellationToken ct)
    {
        await EnsureClinicAsync(clinicId, ct);
        return Found(await _benchmark.GetGenerationAsync(clinicId, id, ct), "Generation");
    }

    public async Task<BenchmarkGenerationSummary> CancelGenerationAsync(Guid clinicId, Guid id, CancellationToken ct)
    {
        await EnsureClinicAsync(clinicId, ct);
        var result = await _benchmark.CancelGenerationAsync(clinicId, id, ct);
        return result.Status switch
        {
            GenerationCancelStatus.NotFound => throw new KeyNotFoundException("Generation not found."),
            GenerationCancelStatus.NotPending => throw new InvalidOperationException(
                $"This generation already finished ({result.Summary?.Status}), so there is nothing to stop."),
            _ => result.Summary ?? throw new KeyNotFoundException("Generation not found.")
        };
    }

    public async Task<BenchmarkRunSummary> StartRunAsync(Guid clinicId, StartBenchmarkRunRequest request, CancellationToken ct)
    {
        await EnsureClinicAsync(clinicId, ct);
        return await _benchmark.StartRunAsync(clinicId, request.Scope, request.GenerationId, ct);
    }

    public async Task<IReadOnlyList<BenchmarkRunSummary>> ListRunsAsync(Guid clinicId, int take, CancellationToken ct)
    {
        await EnsureClinicAsync(clinicId, ct);
        return await _benchmark.ListRunsAsync(clinicId, take, ct);
    }

    public async Task<BenchmarkRunSummary> GetRunAsync(Guid clinicId, Guid id, CancellationToken ct)
    {
        await EnsureClinicAsync(clinicId, ct);
        return Found(await _benchmark.GetRunAsync(clinicId, id, ct), "Run");
    }

    public async Task<BenchmarkResultListResponse> ListResultsAsync(Guid clinicId, Guid runId, string? classification, int skip, int take,
        CancellationToken ct)
    {
        await EnsureClinicAsync(clinicId, ct);
        return Found(await _benchmark.ListResultsAsync(clinicId, runId, classification, skip, take, ct), "Run");
    }

    public async Task<IReadOnlyList<BenchmarkRunGenerationBreakdown>> GetRunGenerationBreakdownAsync(Guid clinicId, Guid runId, CancellationToken ct)
    {
        await EnsureClinicAsync(clinicId, ct);
        return Found(await _benchmark.GetRunGenerationBreakdownAsync(clinicId, runId, ct), "Run");
    }

    public async Task<BenchmarkResultDetailResponse> GetResultAsync(Guid clinicId, Guid runId, Guid resultId, CancellationToken ct)
    {
        await EnsureClinicAsync(clinicId, ct);
        return Found(await _benchmark.GetResultAsync(clinicId, runId, resultId, ct), "Result");
    }

    private async Task EnsureClinicAsync(Guid clinicId, CancellationToken ct)
    {
        if (await _clinics.GetReadOnlyAsync(clinicId, ct) is null) throw new KeyNotFoundException("Clinic not found.");
    }

    private static T Found<T>(T? value, string what) where T : class =>
        value ?? throw new KeyNotFoundException($"{what} not found.");
}
