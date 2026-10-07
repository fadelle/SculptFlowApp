using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Business.Contracts.Services.Configuration;
using PlasticSurgery.Controllers.Filters;
using PlasticSurgery.Entities.Requests.Configuration;

namespace PlasticSurgery.Controllers.Admin;

/// <summary>
/// Platform-admin configuration API (internal; called by the SculptFlowAdmin portal's Configuration page): list the
/// settings declared in ConfigDefaults with their default, stored and effective value; store a value for one
/// (section, key); or reset it to the default. Protected by X-Platform-Admin-Key; X-Admin-Actor is stored as the
/// row's updated_by. Errors: 400 unknown setting or invalid value, 404 nothing stored to reset.
/// </summary>
[ApiController]
[AllowAnonymous]
[RequirePlatformAdminKey]
[Route("api/platform-admin/settings")]
[ApiErrors]
public class SettingsAdminController : ControllerBase
{
    private readonly ISettingsService _settings;

    public SettingsAdminController(ISettingsService settings)
    {
        _settings = settings;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await _settings.ListAsync(ct));

    [HttpPut("{section}/{key}")]
    public async Task<IActionResult> Set(string section, string key, [FromBody] SetSettingRequest request, CancellationToken ct) =>
        Ok(await _settings.SetAsync(section, key, request.Value, request.Note, RequirePlatformAdminKeyAttribute.Actor(Request), ct));

    [HttpDelete("{section}/{key}")]
    public async Task<IActionResult> Reset(string section, string key, CancellationToken ct) =>
        await _settings.ResetAsync(section, key, ct) ? NoContent() : NotFound();
}
