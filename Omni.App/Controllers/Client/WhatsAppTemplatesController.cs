using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.WhatsApp;
using Omni.App.Common.Exceptions;
using Omni.App.Entities.Requests.WhatsApp;
using Omni.App.Entities.Responses.WhatsApp;

namespace Omni.App.Controllers.Client;

[ApiController]
[Route("api/whatsapp/templates")]
public class WhatsAppTemplatesController : DashboardApiController
{
    private readonly IWhatsAppTemplateService _templates;

    public WhatsAppTemplatesController(IWhatsAppTemplateService templates, ICurrentClientContext clientContext) : base(clientContext)
    {
        _templates = templates;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WhatsAppTemplateResponse>>> List(CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var templates = await _templates.ListAsync(clientId.Value, ct);
        return Ok(templates);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WhatsAppTemplateResponse>> GetById(Guid id, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var template = await _templates.GetByIdAsync(clientId.Value, id, ct);
        return template is null ? NotFound() : Ok(template);
    }

    [HttpPost]
    public async Task<ActionResult<WhatsAppTemplateResponse>> Create([FromBody] CreateWhatsAppTemplateRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var template = await _templates.CreateAsync(request with { ClientId = clientId.Value }, ct);
        return CreatedAtAction(nameof(GetById), new { id = template.Id }, template);
    }

    /// <summary>Refreshes Status/RejectionReason from Meta for a template that's already been submitted.</summary>
    [HttpPost("{id:guid}/sync")]
    public async Task<ActionResult<WhatsAppTemplateResponse>> Sync(Guid id, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var template = await _templates.SyncStatusAsync(clientId.Value, id, ct);
        return template is null ? NotFound() : Ok(template);
    }
}
