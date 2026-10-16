using Omni.App.Business.Contracts.Services.Knowledge;
using Omni.App.Business.Contracts.Services.PlatformAdmin;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.KnowledgeBenchmark;
using Omni.App.Entities.Requests.KnowledgeBenchmark;
using Omni.App.Entities.Responses.KnowledgeBenchmark;
using Omni.App.Persistence.Contracts.Clients;

namespace Omni.App.Business.Services.PlatformAdmin;

/// <summary>Checks the client, then hands every call to the benchmark service the client dashboard used to call, so the
/// benchmark's rules (sampling, validation, stale detection, one run/generation at a time) stay in one place. "Not this
/// client's" answers from that service become <see cref="KeyNotFoundException"/>.</summary>
public class KnowledgeBenchmarkAdminService : IKnowledgeBenchmarkAdminService
{
    private readonly IKnowledgeBenchmarkService _benchmark;
    private readonly IClientRepository _clients;

    public KnowledgeBenchmarkAdminService(IKnowledgeBenchmarkService benchmark, IClientRepository clients)
    {
        _benchmark = benchmark;
        _clients = clients;
    }

    public async Task<BenchmarkDashboardResponse> GetDashboardAsync(Guid clientId, CancellationToken ct)
    {
        await EnsureClientAsync(clientId, ct);
        return await _benchmark.GetDashboardAsync(clientId, ct);
    }

    public async Task<BenchmarkCaseListResponse> ListCasesAsync(Guid clientId, BenchmarkCaseFilter filter, int skip, int take, CancellationToken ct)
    {
        await EnsureClientAsync(clientId, ct);
        return await _benchmark.ListCasesAsync(clientId, filter, skip, take, ct);
    }

    public async Task<BenchmarkCaseDetailResponse> GetCaseAsync(Guid clientId, Guid id, CancellationToken ct)
    {
        await EnsureClientAsync(clientId, ct);
        return Found(await _benchmark.GetCaseAsync(clientId, id, ct), "Test case");
    }

    public async Task<BenchmarkCaseResponse> CreateManualCaseAsync(Guid clientId, CreateManualBenchmarkCaseRequest request, CancellationToken ct)
    {
        await EnsureClientAsync(clientId, ct);
        return await _benchmark.CreateManualCaseAsync(clientId, request, ct);
    }

    public async Task<BenchmarkCaseResponse> UpdateCaseQuestionAsync(Guid clientId, Guid id, string? question, CancellationToken ct)
    {
        await EnsureClientAsync(clientId, ct);
        return Found(await _benchmark.UpdateCaseQuestionAsync(clientId, id, question, ct), "Test case");
    }

    public async Task<BenchmarkCaseResponse> SetReviewedAsync(Guid clientId, Guid id, bool reviewed, CancellationToken ct)
    {
        await EnsureClientAsync(clientId, ct);
        return Found(await _benchmark.SetReviewedAsync(clientId, id, reviewed, ct), "Test case");
    }

    public async Task DeleteCaseAsync(Guid clientId, Guid id, CancellationToken ct)
    {
        await EnsureClientAsync(clientId, ct);
        if (!await _benchmark.DeleteCaseAsync(clientId, id, ct)) throw new KeyNotFoundException("Test case not found.");
    }

    public async Task<IReadOnlyList<BenchmarkSourceDocumentResponse>> ListSourceDocumentsAsync(Guid clientId, CancellationToken ct)
    {
        await EnsureClientAsync(clientId, ct);
        return await _benchmark.ListSourceDocumentsAsync(clientId, ct);
    }

    public async Task<IReadOnlyList<BenchmarkSourceChunkResponse>> ListSourceChunksAsync(Guid clientId, Guid documentId, CancellationToken ct)
    {
        await EnsureClientAsync(clientId, ct);
        return Found(await _benchmark.ListSourceChunksAsync(clientId, documentId, ct), "Document");
    }

    public async Task<StartBenchmarkGenerationResponse> StartGenerationAsync(Guid clientId, CancellationToken ct)
    {
        await EnsureClientAsync(clientId, ct);
        return await _benchmark.StartGenerationAsync(clientId, ct);
    }

    public async Task<IReadOnlyList<BenchmarkGenerationSummary>> ListGenerationsAsync(Guid clientId, int take, CancellationToken ct)
    {
        await EnsureClientAsync(clientId, ct);
        return await _benchmark.ListGenerationsAsync(clientId, take, ct);
    }

    public async Task<BenchmarkGenerationDetail> GetGenerationAsync(Guid clientId, Guid id, CancellationToken ct)
    {
        await EnsureClientAsync(clientId, ct);
        return Found(await _benchmark.GetGenerationAsync(clientId, id, ct), "Generation");
    }

    public async Task<BenchmarkGenerationSummary> CancelGenerationAsync(Guid clientId, Guid id, CancellationToken ct)
    {
        await EnsureClientAsync(clientId, ct);
        var result = await _benchmark.CancelGenerationAsync(clientId, id, ct);
        return result.Status switch
        {
            GenerationCancelStatus.NotFound => throw new KeyNotFoundException("Generation not found."),
            GenerationCancelStatus.NotPending => throw new InvalidOperationException(
                $"This generation already finished ({result.Summary?.Status}), so there is nothing to stop."),
            _ => result.Summary ?? throw new KeyNotFoundException("Generation not found.")
        };
    }

    public async Task<BenchmarkRunSummary> StartRunAsync(Guid clientId, StartBenchmarkRunRequest request, CancellationToken ct)
    {
        await EnsureClientAsync(clientId, ct);
        return await _benchmark.StartRunAsync(clientId, request.Scope, request.GenerationId, ct);
    }

    public async Task<IReadOnlyList<BenchmarkRunSummary>> ListRunsAsync(Guid clientId, int take, CancellationToken ct)
    {
        await EnsureClientAsync(clientId, ct);
        return await _benchmark.ListRunsAsync(clientId, take, ct);
    }

    public async Task<BenchmarkRunSummary> GetRunAsync(Guid clientId, Guid id, CancellationToken ct)
    {
        await EnsureClientAsync(clientId, ct);
        return Found(await _benchmark.GetRunAsync(clientId, id, ct), "Run");
    }

    public async Task<BenchmarkResultListResponse> ListResultsAsync(Guid clientId, Guid runId, string? classification, int skip, int take,
        CancellationToken ct)
    {
        await EnsureClientAsync(clientId, ct);
        return Found(await _benchmark.ListResultsAsync(clientId, runId, classification, skip, take, ct), "Run");
    }

    public async Task<IReadOnlyList<BenchmarkRunGenerationBreakdown>> GetRunGenerationBreakdownAsync(Guid clientId, Guid runId, CancellationToken ct)
    {
        await EnsureClientAsync(clientId, ct);
        return Found(await _benchmark.GetRunGenerationBreakdownAsync(clientId, runId, ct), "Run");
    }

    public async Task<BenchmarkResultDetailResponse> GetResultAsync(Guid clientId, Guid runId, Guid resultId, CancellationToken ct)
    {
        await EnsureClientAsync(clientId, ct);
        return Found(await _benchmark.GetResultAsync(clientId, runId, resultId, ct), "Result");
    }

    private async Task EnsureClientAsync(Guid clientId, CancellationToken ct)
    {
        if (await _clients.GetReadOnlyAsync(clientId, ct) is null) throw new KeyNotFoundException("Client not found.");
    }

    private static T Found<T>(T? value, string what) where T : class =>
        value ?? throw new KeyNotFoundException($"{what} not found.");
}
