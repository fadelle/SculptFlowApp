using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Business.Contracts.Services.Automation;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Controllers.Filters;

namespace PlasticSurgery.Controllers.Admin;

/// <summary>
/// Platform-admin API for the SculptFlowAutomation tester (internal; never clinics). Each automation run registers a
/// throwaway clinic through the normal sign-up page, exercises the system as that clinic, then calls DELETE here so
/// nothing it created is left behind. Protected by X-Platform-Admin-Key (RequirePlatformAdminKey).
/// Only an automation clinic can ever be deleted (see <see cref="Common.Statics.AutomationClinics"/>); anything else gets 409 and
/// nothing is touched.
/// </summary>
[ApiController]
[AllowAnonymous]
[RequirePlatformAdminKey]
[Route("api/platform-admin/automation")]
public class AutomationAdminController : ControllerBase
{
    private readonly IAutomationCleanupService _cleanup;
    private readonly ILogger<AutomationAdminController> _logger;

    public AutomationAdminController(IAutomationCleanupService cleanup, ILogger<AutomationAdminController> logger)
    {
        _cleanup = cleanup;
        _logger = logger;
    }

    /// <summary>Automation clinics that still exist (e.g. a run that crashed before cleaning up), oldest first.</summary>
    [HttpGet("clinics")]
    public async Task<IActionResult> ListClinics(CancellationToken ct) => Ok(await _cleanup.ListClinicsAsync(ct));

    [HttpDelete("clinics/{clinicId:guid}")]
    public async Task<IActionResult> DeleteClinic(Guid clinicId, CancellationToken ct)
    {
        switch (await _cleanup.DeleteClinicAsync(clinicId, ct))
        {
            case AutomationDeleteOutcome.NotFound:
                return NotFound();
            case AutomationDeleteOutcome.NotAutomationClinic:
                return Conflict(new { error = "Not an automation test clinic; nothing was deleted." });
            default:
                _logger.LogInformation("Automation clinic {ClinicId} deleted by {Actor}.", clinicId, RequirePlatformAdminKeyAttribute.Actor(Request));
                return NoContent();
        }
    }
}
