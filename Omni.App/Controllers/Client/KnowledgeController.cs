using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Knowledge;
using Omni.App.Entities.Requests.Knowledge;
using Omni.App.Entities.Responses.Knowledge;

namespace Omni.App.Controllers.Client;

/// <summary>Dashboard CRUD for the client Knowledge Base. Login required; the client always comes from
/// the logged-in user (CurrentClientContext via DashboardApiController), never from the request.</summary>
[ApiController]
[Route("api/knowledge")]
public class KnowledgeController : DashboardApiController
{
    private readonly IKnowledgeService _knowledge;
    private readonly IKnowledgeSettingsService _settings;

    public KnowledgeController(IKnowledgeService knowledge, IKnowledgeSettingsService settings, ICurrentClientContext clientContext) : base(clientContext)
    {
        _knowledge = knowledge;
        _settings = settings;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<KnowledgeDocumentResponse>>> List(CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();
        return Ok(await _knowledge.ListAsync(clientId.Value, ct));
    }

    /// <summary>All of the client's Knowledge Base retrieval/embedding settings (created with the system
    /// defaults on first use).</summary>
    [HttpGet("settings")]
    public async Task<ActionResult<KnowledgeSettingsResponse>> GetSettings(CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();
        return Ok(await _settings.GetAsync(clientId.Value, ct));
    }

    /// <summary>Updates only chunk size, chunk overlap, top K and minimum similarity — the request type
    /// has no fields for the read-only settings, so they can't be changed here.</summary>
    [HttpPut("settings")]
    public async Task<ActionResult<KnowledgeSettingsResponse>> UpdateSettings([FromBody] UpdateKnowledgeSettingsRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        return Ok(await _settings.UpdateAsync(clientId.Value, request, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<KnowledgeDocumentResponse>> GetById(Guid id, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();
        var doc = await _knowledge.GetByIdAsync(clientId.Value, id, ct);
        return doc is null ? NotFound() : Ok(doc);
    }

    [HttpPost]
    public async Task<ActionResult<KnowledgeDocumentResponse>> Create([FromBody] SaveKnowledgeRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        try
        {
            var doc = await _knowledge.CreateAsync(clientId.Value, request, ct);
            return CreatedAtAction(nameof(GetById), new { id = doc.Id }, doc);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }

    /// <summary>Creates a document from ONE uploaded PDF/DOCX/TXT (multipart/form-data: file, optional title,
    /// category, isActive). The text is extracted server-side and goes through the same chunk → embed →
    /// store pipeline as a manual entry, so it's searchable immediately. The file itself isn't kept.</summary>
    [HttpPost("upload")]
    public async Task<ActionResult<KnowledgeDocumentResponse>> Upload([FromForm] KnowledgeUploadForm form, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        if (form.File is null) return BadRequest(new { error = "Choose a file to upload." });

        try
        {
            await using var stream = form.File.OpenReadStream();
            var doc = await _knowledge.CreateFromUploadAsync(
                clientId.Value,
                new UploadKnowledgeRequest(form.Title, string.IsNullOrWhiteSpace(form.Category) ? "general" : form.Category, form.IsActive),
                form.File.FileName, stream, form.File.Length, ct);
            return CreatedAtAction(nameof(GetById), new { id = doc.Id }, doc);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<KnowledgeDocumentResponse>> Update(Guid id, [FromBody] SaveKnowledgeRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        try
        {
            var doc = await _knowledge.UpdateAsync(clientId.Value, id, request, ct);
            return doc is null ? NotFound() : Ok(doc);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/active")]
    public async Task<ActionResult<KnowledgeDocumentResponse>> SetActive(Guid id, [FromBody] SetKnowledgeActiveRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();
        var doc = await _knowledge.SetActiveAsync(clientId.Value, id, request.IsActive, ct);
        return doc is null ? NotFound() : Ok(doc);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();
        return await _knowledge.DeleteAsync(clientId.Value, id, ct) ? NoContent() : NotFound();
    }
}
