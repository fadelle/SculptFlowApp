using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Services.PlatformAdmin;
using Omni.App.Controllers.Filters;
using Omni.App.Entities.Dtos.KnowledgeBenchmark;
using Omni.App.Entities.Requests.KnowledgeBenchmark;

namespace Omni.App.Controllers.Admin;

/// <summary>
/// Platform-admin Knowledge Retrieval Benchmark API (internal; called by the Omni.Admin portal's Search benchmark
/// page). Every route names the client; the benchmark is no longer reachable from the client dashboard. Writes answer the
/// changed record (the portal files its audit under the client in the route). Errors: 400 invalid input, 404 unknown
/// client/case/generation/run, 409 a run or generation is already in progress, 422 the generation already finished,
/// 502/503 the n8n question generator failed or isn't configured.
/// </summary>
[ApiController]
[AllowAnonymous]
[RequirePlatformAdminKey]
[Route("api/platform-admin/knowledge-benchmark/clients/{clientId:guid}")]
[ApiErrors]
public class KnowledgeBenchmarkAdminController : ControllerBase
{
    private readonly IKnowledgeBenchmarkAdminService _benchmark;

    public KnowledgeBenchmarkAdminController(IKnowledgeBenchmarkAdminService benchmark)
    {
        _benchmark = benchmark;
    }

    /// <summary>Case counts, the current settings, the latest run and the latest completed run.</summary>
    [HttpGet]
    public async Task<IActionResult> Dashboard(Guid clientId, CancellationToken ct) => Ok(await _benchmark.GetDashboardAsync(clientId, ct));

    // ------------------------------- cases -------------------------------

    /// <summary>view = all | generated | manual | reviewed | unreviewed | stale; generationId limits it to one generation.</summary>
    [HttpGet("cases")]
    public async Task<IActionResult> ListCases(Guid clientId, [FromQuery] string? view, [FromQuery] Guid? generationId,
        [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default) =>
        Ok(await _benchmark.ListCasesAsync(clientId, new BenchmarkCaseFilter(view, generationId), Math.Max(skip, 0), take, ct));

    [HttpGet("cases/{id:guid}")]
    public async Task<IActionResult> GetCase(Guid clientId, Guid id, CancellationToken ct) => Ok(await _benchmark.GetCaseAsync(clientId, id, ct));

    [HttpPost("cases")]
    public async Task<IActionResult> CreateManualCase(Guid clientId, [FromBody] CreateManualBenchmarkCaseRequest request, CancellationToken ct) =>
        Ok(await _benchmark.CreateManualCaseAsync(clientId, request, ct));

    [HttpPut("cases/{id:guid}")]
    public async Task<IActionResult> UpdateCase(Guid clientId, Guid id, [FromBody] UpdateBenchmarkCaseRequest request, CancellationToken ct) =>
        Ok(await _benchmark.UpdateCaseQuestionAsync(clientId, id, request.Question, ct));

    [HttpPost("cases/{id:guid}/review")]
    public async Task<IActionResult> SetReviewed(Guid clientId, Guid id, [FromBody] SetBenchmarkCaseReviewedRequest request, CancellationToken ct) =>
        Ok(await _benchmark.SetReviewedAsync(clientId, id, request.Reviewed, ct));

    [HttpDelete("cases/{id:guid}")]
    public async Task<IActionResult> DeleteCase(Guid clientId, Guid id, CancellationToken ct)
    {
        await _benchmark.DeleteCaseAsync(clientId, id, ct);
        return NoContent();
    }

    /// <summary>Samples the client's active chunks and hands them to the n8n question generator. 202 + the generationId while
    /// n8n works (it calls back on KnowledgeBenchmarkIngestController); 200 when nothing was sent or n8n answered at once.</summary>
    [HttpPost("cases/generate")]
    public async Task<IActionResult> GenerateCases(Guid clientId, CancellationToken ct)
    {
        var started = await _benchmark.StartGenerationAsync(clientId, ct);
        return started.Status == "pending" ? Accepted(started) : Ok(started);
    }

    // --------------------- pickers for adding a manual case ---------------------

    [HttpGet("source-documents")]
    public async Task<IActionResult> SourceDocuments(Guid clientId, CancellationToken ct) => Ok(await _benchmark.ListSourceDocumentsAsync(clientId, ct));

    [HttpGet("source-documents/{documentId:guid}/chunks")]
    public async Task<IActionResult> SourceChunks(Guid clientId, Guid documentId, CancellationToken ct) =>
        Ok(await _benchmark.ListSourceChunksAsync(clientId, documentId, ct));

    // ---------------------------- generations ----------------------------

    [HttpGet("generations")]
    public async Task<IActionResult> ListGenerations(Guid clientId, [FromQuery] int take = 20, CancellationToken ct = default) =>
        Ok(await _benchmark.ListGenerationsAsync(clientId, take, ct));

    /// <summary>What was sent, what came back, what was rejected (with reasons) and n8n's raw reply.</summary>
    [HttpGet("generations/{id:guid}")]
    public async Task<IActionResult> GetGeneration(Guid clientId, Guid id, CancellationToken ct) => Ok(await _benchmark.GetGenerationAsync(clientId, id, ct));

    /// <summary>"Stop generating": frees Generate and refuses n8n's late reply (n8n's own run can't be halted from here).</summary>
    [HttpPost("generations/{id:guid}/cancel")]
    public async Task<IActionResult> CancelGeneration(Guid clientId, Guid id, CancellationToken ct) =>
        Ok(await _benchmark.CancelGenerationAsync(clientId, id, ct));

    // ------------------------------- runs -------------------------------

    /// <summary>Queues a run (scope = all | generated | reviewed | generation; generation needs generationId) and answers 202;
    /// the background worker runs it — poll GET runs/{id}.</summary>
    [HttpPost("runs")]
    public async Task<IActionResult> StartRun(Guid clientId, [FromBody] StartBenchmarkRunRequest request, CancellationToken ct) =>
        Accepted(await _benchmark.StartRunAsync(clientId, request, ct));

    [HttpGet("runs")]
    public async Task<IActionResult> ListRuns(Guid clientId, [FromQuery] int take = 20, CancellationToken ct = default) =>
        Ok(await _benchmark.ListRunsAsync(clientId, take, ct));

    [HttpGet("runs/{id:guid}")]
    public async Task<IActionResult> GetRun(Guid clientId, Guid id, CancellationToken ct) => Ok(await _benchmark.GetRunAsync(clientId, id, ct));

    /// <summary>classification = EXACT_CHUNK_HIT | DOCUMENT_ONLY_HIT | MISS | STALE_CASE | ERROR (omit for all).</summary>
    [HttpGet("runs/{id:guid}/results")]
    public async Task<IActionResult> ListResults(Guid clientId, Guid id, [FromQuery] string? classification, [FromQuery] int skip = 0,
        [FromQuery] int take = 100, CancellationToken ct = default) =>
        Ok(await _benchmark.ListResultsAsync(clientId, id, classification, Math.Max(skip, 0), take, ct));

    /// <summary>The run's scores split by the generation each case came from ("no generation" = manual cases).</summary>
    [HttpGet("runs/{id:guid}/generations")]
    public async Task<IActionResult> RunGenerations(Guid clientId, Guid id, CancellationToken ct) =>
        Ok(await _benchmark.GetRunGenerationBreakdownAsync(clientId, id, ct));

    /// <summary>One result with the ordered chunks retrieval returned (the failed-case analysis).</summary>
    [HttpGet("runs/{id:guid}/results/{resultId:guid}")]
    public async Task<IActionResult> GetResult(Guid clientId, Guid id, Guid resultId, CancellationToken ct) =>
        Ok(await _benchmark.GetResultAsync(clientId, id, resultId, ct));
}
