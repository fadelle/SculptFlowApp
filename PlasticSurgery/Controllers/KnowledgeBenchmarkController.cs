using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Dtos;
using PlasticSurgery.Integrations.Knowledge.Benchmark;
using PlasticSurgery.Services;

namespace PlasticSurgery.Controllers;

/// <summary>
/// Knowledge Retrieval Benchmark API (dashboard-facing). Deliberately its own controller, separate from
/// KnowledgeController: the benchmark is a diagnostic feature that CALLS the production retrieval engine and must never
/// be part of it. Thin by design — all logic lives in IKnowledgeBenchmarkService. Like every dashboard controller it
/// requires a logged-in session and takes the clinic from ICurrentClinicContext; no request carries a clinicId, and an id
/// belonging to another clinic is simply "not found".
/// </summary>
[ApiController]
[Route("api/knowledge/benchmark")]
public class KnowledgeBenchmarkController : DashboardApiController
{
    private readonly IKnowledgeBenchmarkService _benchmark;

    public KnowledgeBenchmarkController(IKnowledgeBenchmarkService benchmark, ICurrentClinicContext clinicContext) : base(clinicContext)
    {
        _benchmark = benchmark;
    }

    /// <summary>Dashboard numbers: case counts, the current settings, the latest run and the latest completed run.</summary>
    [HttpGet]
    public async Task<ActionResult<BenchmarkDashboardResponse>> Dashboard(CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        return Ok(await _benchmark.GetDashboardAsync(clinicId.Value, ct));
    }

    // ------------------------------- cases -------------------------------

    /// <summary>view = all | generated | manual | reviewed | unreviewed | stale. generationId (optional) limits the list to the
    /// cases one "Generate Test Cases" request produced.</summary>
    [HttpGet("cases")]
    public async Task<ActionResult<BenchmarkCaseListResponse>> ListCases(
        [FromQuery] string? view, [FromQuery] Guid? generationId, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        return Ok(await _benchmark.ListCasesAsync(clinicId.Value, new BenchmarkCaseFilter(view, generationId), skip, take, ct));
    }

    [HttpGet("cases/{id:guid}")]
    public async Task<ActionResult<BenchmarkCaseDetailResponse>> GetCase(Guid id, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        var detail = await _benchmark.GetCaseAsync(clinicId.Value, id, ct);
        return detail is null ? NotFound() : Ok(detail);
    }

    /// <summary>Samples 20 of THIS clinic's active chunks, records a pending generation and hands the chunks to the n8n benchmark
    /// generator — then returns at once: 202 + the generationId. n8n calls back with the questions
    /// (KnowledgeBenchmarkIngestController); poll GET generations/{id}. 200 means nothing was sent (nothing to sample) or the
    /// workflow answered synchronously. 503 = generator webhook not configured, 502 = the generator couldn't be reached/failed,
    /// 409 = another generation is still waiting for its callback.</summary>
    [HttpPost("cases/generate")]
    public async Task<ActionResult<StartBenchmarkGenerationResponse>> GenerateCases(CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        try
        {
            var started = await _benchmark.StartGenerationAsync(clinicId.Value, ct);
            return started.Status == "pending" ? Accepted(started) : Ok(started);
        }
        catch (BenchmarkGeneratorNotConfiguredException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
        catch (BenchmarkGenerationInProgressException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (BenchmarkGenerationException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
        }
    }

    // ---------------------------- generations ----------------------------

    /// <summary>Recent generations (newest first) with counts and the scores of their cases in the latest run that included them.</summary>
    [HttpGet("generations")]
    public async Task<ActionResult<IReadOnlyList<BenchmarkGenerationSummary>>> ListGenerations([FromQuery] int take = 20, CancellationToken ct = default)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        return Ok(await _benchmark.ListGenerationsAsync(clinicId.Value, take, ct));
    }

    /// <summary>One generation: what was sent, what came back, what was rejected (with reasons) and n8n's raw reply.</summary>
    [HttpGet("generations/{id:guid}")]
    public async Task<ActionResult<BenchmarkGenerationDetail>> GetGeneration(Guid id, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        var detail = await _benchmark.GetGenerationAsync(clinicId.Value, id, ct);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPost("cases")]
    public async Task<ActionResult<BenchmarkCaseResponse>> CreateManualCase([FromBody] CreateManualBenchmarkCaseRequest request, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        try
        {
            return Ok(await _benchmark.CreateManualCaseAsync(clinicId.Value, request, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("cases/{id:guid}")]
    public async Task<ActionResult<BenchmarkCaseResponse>> UpdateCase(Guid id, [FromBody] UpdateBenchmarkCaseRequest request, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        try
        {
            var updated = await _benchmark.UpdateCaseQuestionAsync(clinicId.Value, id, request.Question, ct);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("cases/{id:guid}/review")]
    public async Task<ActionResult<BenchmarkCaseResponse>> SetReviewed(Guid id, [FromBody] SetBenchmarkCaseReviewedRequest request, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        var updated = await _benchmark.SetReviewedAsync(clinicId.Value, id, request.Reviewed, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("cases/{id:guid}")]
    public async Task<IActionResult> DeleteCase(Guid id, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        return await _benchmark.DeleteCaseAsync(clinicId.Value, id, ct) ? NoContent() : NotFound();
    }

    // --------------------- pickers for adding a manual case ---------------------

    [HttpGet("source-documents")]
    public async Task<ActionResult<IReadOnlyList<BenchmarkSourceDocumentResponse>>> SourceDocuments(CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        return Ok(await _benchmark.ListSourceDocumentsAsync(clinicId.Value, ct));
    }

    [HttpGet("source-documents/{documentId:guid}/chunks")]
    public async Task<ActionResult<IReadOnlyList<BenchmarkSourceChunkResponse>>> SourceChunks(Guid documentId, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        var chunks = await _benchmark.ListSourceChunksAsync(clinicId.Value, documentId, ct);
        return chunks is null ? NotFound() : Ok(chunks);
    }

    // ------------------------------- runs -------------------------------

    /// <summary>Queues a run (scope = all | generated | reviewed) and returns 202 immediately; the run executes in the
    /// background — poll GET runs/{id}. 409 = a run is already in progress.</summary>
    [HttpPost("runs")]
    public async Task<ActionResult<BenchmarkRunSummary>> StartRun([FromBody] StartBenchmarkRunRequest request, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        try
        {
            var run = await _benchmark.StartRunAsync(clinicId.Value, request.Scope, ct);
            return Accepted(run);
        }
        catch (BenchmarkRunInProgressException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("runs")]
    public async Task<ActionResult<IReadOnlyList<BenchmarkRunSummary>>> ListRuns([FromQuery] int take = 20, CancellationToken ct = default)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        return Ok(await _benchmark.ListRunsAsync(clinicId.Value, take, ct));
    }

    [HttpGet("runs/{id:guid}")]
    public async Task<ActionResult<BenchmarkRunSummary>> GetRun(Guid id, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        var run = await _benchmark.GetRunAsync(clinicId.Value, id, ct);
        return run is null ? NotFound() : Ok(run);
    }

    /// <summary>classification = EXACT_CHUNK_HIT | DOCUMENT_ONLY_HIT | MISS | STALE_CASE | ERROR (omit for all).</summary>
    [HttpGet("runs/{id:guid}/results")]
    public async Task<ActionResult<BenchmarkResultListResponse>> ListResults(
        Guid id, [FromQuery] string? classification, [FromQuery] int skip = 0, [FromQuery] int take = 100, CancellationToken ct = default)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        var results = await _benchmark.ListResultsAsync(clinicId.Value, id, classification, skip, take, ct);
        return results is null ? NotFound() : Ok(results);
    }

    /// <summary>The run's scores split by the generation each case came from ("no generation" = manual cases).</summary>
    [HttpGet("runs/{id:guid}/generations")]
    public async Task<ActionResult<IReadOnlyList<BenchmarkRunGenerationBreakdown>>> RunGenerations(Guid id, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        var breakdown = await _benchmark.GetRunGenerationBreakdownAsync(clinicId.Value, id, ct);
        return breakdown is null ? NotFound() : Ok(breakdown);
    }

    /// <summary>One result with the ordered chunks retrieval returned — the failed-case analysis view.</summary>
    [HttpGet("runs/{id:guid}/results/{resultId:guid}")]
    public async Task<ActionResult<BenchmarkResultDetailResponse>> GetResult(Guid id, Guid resultId, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        var detail = await _benchmark.GetResultAsync(clinicId.Value, id, resultId, ct);
        return detail is null ? NotFound() : Ok(detail);
    }
}
