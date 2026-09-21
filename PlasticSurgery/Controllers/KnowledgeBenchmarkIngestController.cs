using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Dtos;
using PlasticSurgery.Integrations.Knowledge.Benchmark;

namespace PlasticSurgery.Controllers;

/// <summary>
/// The endpoint the benchmark's n8n question-generator workflow calls when it has finished writing the questions:
/// <c>POST /api/knowledge/benchmark/generations/{generationId}/questions</c> with the <c>X-Ingest-Key</c> header, like every other
/// server-to-server call from n8n. A separate controller from KnowledgeBenchmarkController because that one is cookie-login
/// only — n8n has no session.
///
/// Trust model: the request supplies NO clinic. The generationId in the URL names a generation this app created (and stored
/// with the exact chunks it sent); the clinic and the allowed chunk set come from that stored row, and every returned id is
/// validated against it and the live Knowledge Base. An unknown, already-completed or expired generationId is refused, so a
/// callback can never write outside the clinic that requested the generation.
/// </summary>
[ApiController]
[Route("api/knowledge/benchmark/generations")]
[RequireIngestKey]
public class KnowledgeBenchmarkIngestController : ControllerBase
{
    private readonly IKnowledgeBenchmarkService _benchmark;

    public KnowledgeBenchmarkIngestController(IKnowledgeBenchmarkService benchmark)
    {
        _benchmark = benchmark;
    }

    /// <summary>Body: <c>{ "generationId": "…" (optional, must equal the URL's), "questions": [ { documentId, chunkId, question } ] }</c>
    /// (also accepted wrapped in a one-element array, or as a bare array). 200 = stored (repeat deliveries answer 200 with
    /// alreadyProcessed = true and change nothing), 400 = unreadable body / generationId mismatch, 404 = unknown generation,
    /// 409 = the generation failed or expired.</summary>
    [HttpPost("{generationId:guid}/questions")]
    public async Task<ActionResult<BenchmarkGenerationCallbackResponse>> ReceiveQuestions(
        Guid generationId, [FromBody] JsonElement body, CancellationToken ct)
    {
        var rawBody = body.GetRawText();

        GeneratorResponse reply;
        try
        {
            reply = GeneratorResponseParser.Parse(rawBody);
        }
        catch (BenchmarkGenerationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        GenerationReceiveResult result;
        try
        {
            result = await _benchmark.ReceiveGenerationResultAsync(generationId, reply, rawBody, requireEcho: false, ct);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return result.Status switch
        {
            GenerationReceiveStatus.NotFound => NotFound(new { error = result.Message }),
            GenerationReceiveStatus.NotAccepting => Conflict(new { error = result.Message }),
            _ => Ok(new BenchmarkGenerationCallbackResponse(
                generationId,
                result.Summary?.Status ?? BenchmarkGenerationStatusNames.Completed,
                result.Summary?.CasesCreated ?? 0,
                result.Summary?.RejectedCount ?? 0,
                result.Status == GenerationReceiveStatus.AlreadyProcessed))
        };
    }
}

/// <summary>Status names used in responses (kept next to the controller so the wire values are visible in one place).</summary>
internal static class BenchmarkGenerationStatusNames
{
    public const string Completed = "completed";
}
