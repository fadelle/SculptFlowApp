using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Services.PlatformAdmin;
using Omni.App.Controllers.Filters;
using Omni.App.Entities.Requests.PlatformAdmin;

namespace Omni.App.Controllers.Admin;

/// <summary>
/// Platform-admin content API (internal; called by the Omni.Admin portal): campaigns, WhatsApp templates,
/// services and the knowledge base across clients. Writes answer { clientId }. Errors: 400 invalid input, 404 not
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
    public async Task<IActionResult> Campaigns([FromQuery] Guid? clientId, [FromQuery] string? status, [FromQuery] int page, CancellationToken ct) =>
        Ok(await _content.ListCampaignsAsync(clientId, status, page, ct));

    [HttpGet("campaigns/{id:guid}")]
    public async Task<IActionResult> Campaign(Guid id, CancellationToken ct) =>
        await _content.GetCampaignAsync(id, ct) is { } campaign ? Ok(campaign) : NotFound();

    [HttpGet("campaigns/{id:guid}/recipients")]
    public async Task<IActionResult> Recipients(Guid id, [FromQuery] string? status, [FromQuery] int page, CancellationToken ct) =>
        Ok(await _content.RecipientsAsync(id, status, page, ct));

    [HttpPost("campaigns/{id:guid}/cancel")]
    public async Task<IActionResult> CancelCampaign(Guid id, CancellationToken ct) => Ok(await _content.CancelCampaignAsync(id, ct));

    [HttpGet("templates")]
    public async Task<IActionResult> Templates([FromQuery] Guid? clientId, [FromQuery] string? status, CancellationToken ct) =>
        Ok(await _content.ListTemplatesAsync(clientId, status, ct));

    [HttpGet("services")]
    public async Task<IActionResult> Services([FromQuery] Guid? clientId, CancellationToken ct) => Ok(await _content.ListServicesAsync(clientId, ct));

    [HttpPut("services/{id:guid}/active")]
    public async Task<IActionResult> SetServiceActive(Guid id, [FromBody] ActiveBody request, CancellationToken ct) =>
        Ok(await _content.SetServiceActiveAsync(id, request.Active, ct));

    [HttpGet("knowledge")]
    public async Task<IActionResult> Documents([FromQuery] Guid? clientId, [FromQuery] string? search, [FromQuery] bool? active,
        [FromQuery] int page, CancellationToken ct) =>
        Ok(await _content.ListDocumentsAsync(clientId, search, active, page, ct));

    [HttpGet("knowledge/{id:guid}")]
    public async Task<IActionResult> Document(Guid id, CancellationToken ct) =>
        await _content.GetDocumentAsync(id, ct) is { } doc ? Ok(doc) : NotFound();

    [HttpPut("knowledge/{id:guid}/active")]
    public async Task<IActionResult> SetDocumentActive(Guid id, [FromBody] ActiveBody request, CancellationToken ct) =>
        Ok(await _content.SetDocumentActiveAsync(id, request.Active, ct));

    [HttpGet("knowledge/websites")]
    public async Task<IActionResult> Websites([FromQuery] Guid? clientId, CancellationToken ct) => Ok(await _content.ListWebsitesAsync(clientId, ct));

    [HttpGet("knowledge/settings/{clientId:guid}")]
    public async Task<IActionResult> SearchSettings(Guid clientId, CancellationToken ct) =>
        await _content.SearchSettingsAsync(clientId, ct) is { } settings ? Ok(settings) : NotFound();

    [HttpPut("knowledge/settings/{clientId:guid}")]
    public async Task<IActionResult> UpdateSearchSettings(Guid clientId, [FromBody] SearchSettingsBody request, CancellationToken ct) =>
        Ok(await _content.UpdateSearchSettingsAsync(clientId, request.TopK, request.MinimumSimilarity, ct));
}
