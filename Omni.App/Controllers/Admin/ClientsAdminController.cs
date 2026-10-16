using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Services.PlatformAdmin;
using Omni.App.Controllers.Filters;
using Omni.App.Entities.Requests.Inbox;
using Omni.App.Entities.Requests.PlatformAdmin;

namespace Omni.App.Controllers.Admin;

/// <summary>
/// Platform-admin clients API (internal; called by the Omni.Admin portal). Writes answer { clientId } so the portal
/// can file its audit entry. Errors: 400 invalid input, 404 no such client.
/// </summary>
[ApiController]
[AllowAnonymous]
[RequirePlatformAdminKey]
[Route("api/platform-admin/clients")]
[ApiErrors]
public class ClientsAdminController : ControllerBase
{
    private readonly IClientAdminService _clients;

    public ClientsAdminController(IClientAdminService clients)
    {
        _clients = clients;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] bool? active, [FromQuery] int page, CancellationToken ct) =>
        Ok(await _clients.ListAsync(search, active, page, ct));

    [HttpGet("options")]
    public async Task<IActionResult> Options(CancellationToken ct) => Ok(await _clients.OptionsAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        await _clients.GetAsync(id, ct) is { } client ? Ok(client) : NotFound();

    [HttpGet("{id:guid}/counts")]
    public async Task<IActionResult> Counts(Guid id, CancellationToken ct) => Ok(await _clients.CountsAsync(id, ct));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ClientUpdate request, CancellationToken ct) =>
        Ok(await _clients.UpdateAsync(id, request, ct));

    [HttpPut("{id:guid}/active")]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] ActiveBody request, CancellationToken ct) =>
        Ok(await _clients.SetActiveAsync(id, request.Active, ct));

    /// <summary>{ planIncludesBot, newConversations, reopenedConversations, botAnswersNew, botAnswersReopened }.</summary>
    [HttpGet("{id:guid}/inbox-settings")]
    public async Task<IActionResult> GetInboxSettings(Guid id, CancellationToken ct) => Ok(await _clients.GetInboxSettingsAsync(id, ct));

    /// <summary>Body { newConversations, reopenedConversations }: whether the AI bot answers new / reopened conversations.</summary>
    [HttpPut("{id:guid}/inbox-settings")]
    public async Task<IActionResult> UpdateInboxSettings(Guid id, [FromBody] UpdateInboxSettingsRequest request, CancellationToken ct) =>
        Ok(await _clients.UpdateInboxSettingsAsync(id, request, ct));
}
