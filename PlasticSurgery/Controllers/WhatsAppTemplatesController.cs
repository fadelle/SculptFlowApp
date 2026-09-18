using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Controllers;

[ApiController]
[Route("api/whatsapp/templates")]
public class WhatsAppTemplatesController : DashboardApiController
{
    private readonly IWhatsAppTemplateService _templates;

    public WhatsAppTemplatesController(IWhatsAppTemplateService templates, ICurrentClinicContext clinicContext) : base(clinicContext)
    {
        _templates = templates;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WhatsAppTemplateResponse>>> List(CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        var templates = await _templates.ListAsync(clinicId.Value, ct);
        return Ok(templates);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WhatsAppTemplateResponse>> GetById(Guid id, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        var template = await _templates.GetByIdAsync(clinicId.Value, id, ct);
        return template is null ? NotFound() : Ok(template);
    }

    [HttpPost]
    public async Task<ActionResult<WhatsAppTemplateResponse>> Create([FromBody] CreateWhatsAppTemplateRequest request, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        try
        {
            var template = await _templates.CreateAsync(request with { ClinicId = clinicId.Value }, ct);
            return CreatedAtAction(nameof(GetById), new { id = template.Id }, template);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Refreshes Status/RejectionReason from Meta for a template that's already been submitted.</summary>
    [HttpPost("{id:guid}/sync")]
    public async Task<ActionResult<WhatsAppTemplateResponse>> Sync(Guid id, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        try
        {
            var template = await _templates.SyncStatusAsync(clinicId.Value, id, ct);
            return template is null ? NotFound() : Ok(template);
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
        catch (MetaGraphApiException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
        }
    }
}
