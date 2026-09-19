using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Controllers;

/// <summary>Dashboard CRUD for the clinic Knowledge Base. Login required; the clinic always comes from
/// the logged-in user (CurrentClinicContext via DashboardApiController), never from the request.</summary>
[ApiController]
[Route("api/knowledge")]
public class KnowledgeController : DashboardApiController
{
    private readonly IKnowledgeService _knowledge;

    public KnowledgeController(IKnowledgeService knowledge, ICurrentClinicContext clinicContext) : base(clinicContext)
    {
        _knowledge = knowledge;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<KnowledgeDocumentResponse>>> List(CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        return Ok(await _knowledge.ListAsync(clinicId.Value, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<KnowledgeDocumentResponse>> GetById(Guid id, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        var doc = await _knowledge.GetByIdAsync(clinicId.Value, id, ct);
        return doc is null ? NotFound() : Ok(doc);
    }

    [HttpPost]
    public async Task<ActionResult<KnowledgeDocumentResponse>> Create([FromBody] SaveKnowledgeRequest request, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        try
        {
            var doc = await _knowledge.CreateAsync(clinicId.Value, request, ct);
            return CreatedAtAction(nameof(GetById), new { id = doc.Id }, doc);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<KnowledgeDocumentResponse>> Update(Guid id, [FromBody] SaveKnowledgeRequest request, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        try
        {
            var doc = await _knowledge.UpdateAsync(clinicId.Value, id, request, ct);
            return doc is null ? NotFound() : Ok(doc);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/active")]
    public async Task<ActionResult<KnowledgeDocumentResponse>> SetActive(Guid id, [FromBody] SetKnowledgeActiveRequest request, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        var doc = await _knowledge.SetActiveAsync(clinicId.Value, id, request.IsActive, ct);
        return doc is null ? NotFound() : Ok(doc);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        return await _knowledge.DeleteAsync(clinicId.Value, id, ct) ? NoContent() : NotFound();
    }
}
