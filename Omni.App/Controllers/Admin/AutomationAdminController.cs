using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Services.Automation;
using Omni.App.Common.Enums;
using Omni.App.Controllers.Filters;

namespace Omni.App.Controllers.Admin;

/// <summary>
/// Platform-admin API for the Omni.Automation tester (internal; never clients). Each automation run registers a
/// throwaway client through the normal sign-up page, exercises the system as that client, then calls DELETE here so
/// nothing it created is left behind. Protected by X-Platform-Admin-Key (RequirePlatformAdminKey).
/// Only an automation client can ever be deleted (see <see cref="Common.Statics.AutomationClients"/>); anything else gets 409 and
/// nothing is touched.
/// </summary>
[ApiController]
[AllowAnonymous]
[RequirePlatformAdminKey]
[Route("api/platform-admin/automation")]
[ApiErrors]
public class AutomationAdminController : ControllerBase
{
    private readonly IAutomationCleanupService _cleanup;
    private readonly ILogger<AutomationAdminController> _logger;

    public AutomationAdminController(IAutomationCleanupService cleanup, ILogger<AutomationAdminController> logger)
    {
        _cleanup = cleanup;
        _logger = logger;
    }

    /// <summary>Automation clients that still exist (e.g. a run that crashed before cleaning up), oldest first.</summary>
    [HttpGet("clients")]
    public async Task<IActionResult> ListClients(CancellationToken ct) => Ok(await _cleanup.ListClientsAsync(ct));

    [HttpDelete("clients/{clientId:guid}")]
    public async Task<IActionResult> DeleteClient(Guid clientId, CancellationToken ct)
    {
        switch (await _cleanup.DeleteClientAsync(clientId, ct))
        {
            case AutomationDeleteOutcome.NotFound:
                return NotFound();
            case AutomationDeleteOutcome.NotAutomationClient:
                return Conflict(new { error = "Not an automation test client; nothing was deleted." });
            default:
                _logger.LogInformation("Automation client {ClientId} deleted by {Actor}.", clientId, RequirePlatformAdminKeyAttribute.Actor(Request));
                return NoContent();
        }
    }
}
