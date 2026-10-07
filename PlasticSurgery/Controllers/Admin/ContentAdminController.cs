using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Business.Contracts.Services.PlatformAdmin;
using PlasticSurgery.Controllers.Filters;
using PlasticSurgery.Entities.Requests.PlatformAdmin;

namespace PlasticSurgery.Controllers.Admin;

/// <summary>
/// Platform-admin content API (internal; called by the SculptFlowAdmin portal): campaigns, WhatsApp templates,
/// procedures and the knowledge base across clinics. Writes answer { clinicId }. Errors: 400 invalid input, 404 not
/// found, 422 not allowed now (e.g. cancelling a finished campaign).
/// </summary>
[ApiController]
[AllowAnonymous]
[RequirePlatformAdminKey]
[Route("api/platform-admin/content")]
[ApiErrors]
public class ContentAdminController : ControllerBase
{
    private readonly IContentAdminService _content;

    public ContentAdminController(IContentAdminService content)
    {
        _content = content;
    }

    [HttpGet("campaigns")]
    public async Task<IActionResult> Campaigns([FromQuery] Guid? clinicId, [FromQuery] string? status, [FromQuery] int page, CancellationToken ct) =>
        Ok(await _content.ListCampaignsAsync(clinicId, status, page, ct));

    [HttpGet("campaigns/{id:guid}")]
    public async Task<IActionResult> Campaign(Guid id, CancellationToken ct) =>
        await _content.GetCampaignAsync(id, ct) is { } campaign ? Ok(campaign) : NotFound();

    [HttpGet("campaigns/{id:guid}/recipients")]
    public async Task<IActionResult> Recipients(Guid id, [FromQuery] string? status, [FromQuery] int page, CancellationToken ct) =>
        Ok(await _content.RecipientsAsync(id, status, page, ct));

    [HttpPost("campaigns/{id:guid}/cancel")]
    public async Task<IActionResult> CancelCampaign(Guid id, CancellationToken ct) => Ok(await _content.CancelCampaignAsync(id, ct));

    [HttpGet("templates")]
    public async Task<IActionResult> Templates([FromQuery] Guid? clinicId, [FromQuery] string? status, CancellationToken ct) =>
        Ok(await _content.ListTemplatesAsync(clinicId, status, ct));

    [HttpGet("procedures")]
    public async Task<IActionResult> Procedures([FromQuery] Guid? clinicId, CancellationToken ct) => Ok(await _content.ListProceduresAsync(clinicId, ct));

    [HttpPut("procedures/{id:guid}/active")]
    public async Task<IActionResult> SetProcedureActive(Guid id, [FromBody] ActiveBody request, CancellationToken ct) =>
        Ok(await _content.SetProcedureActiveAsync(id, request.Active, ct));

    [HttpGet("knowledge")]
    public async Task<IActionResult> Documents([FromQuery] Guid? clinicId, [FromQuery] string? search, [FromQuery] bool? active,
        [FromQuery] int page, CancellationToken ct) =>
        Ok(await _content.ListDocumentsAsync(clinicId, search, active, page, ct));

    [HttpGet("knowledge/{id:guid}")]
    public async Task<IActionResult> Document(Guid id, CancellationToken ct) =>
        await _content.GetDocumentAsync(id, ct) is { } doc ? Ok(doc) : NotFound();

    [HttpPut("knowledge/{id:guid}/active")]
    public async Task<IActionResult> SetDocumentActive(Guid id, [FromBody] ActiveBody request, CancellationToken ct) =>
        Ok(await _content.SetDocumentActiveAsync(id, request.Active, ct));

    [HttpGet("knowledge/websites")]
    public async Task<IActionResult> Websites([FromQuery] Guid? clinicId, CancellationToken ct) => Ok(await _content.ListWebsitesAsync(clinicId, ct));

    [HttpGet("knowledge/settings/{clinicId:guid}")]
    public async Task<IActionResult> SearchSettings(Guid clinicId, CancellationToken ct) =>
        await _content.SearchSettingsAsync(clinicId, ct) is { } settings ? Ok(settings) : NotFound();

    [HttpPut("knowledge/settings/{clinicId:guid}")]
    public async Task<IActionResult> UpdateSearchSettings(Guid clinicId, [FromBody] SearchSettingsBody request, CancellationToken ct) =>
        Ok(await _content.UpdateSearchSettingsAsync(clinicId, request.TopK, request.MinimumSimilarity, ct));
}
