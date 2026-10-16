using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Services.PlatformAdmin;
using Omni.App.Common.Statics;
using Omni.App.Controllers.Filters;

namespace Omni.App.Controllers.Admin;

/// <summary>
/// Platform-admin API timings (internal; called by the Omni.Admin portal's API timings page). Read-only.
/// GET ?hours=24&amp;sort=slowest&amp;take=50 returns the per-route summary for the last N hours.
/// sort: slowest | p95 | max | calls | errors | slow | time. Errors: 400 unknown sort.
/// </summary>
[ApiController]
[AllowAnonymous]
[RequirePlatformAdminKey]
[Route("api/platform-admin/api-timings")]
[ApiErrors]
public class ApiTimingsAdminController : ControllerBase
{
    private readonly IApiTimingAdminService _timings;

    public ApiTimingsAdminController(IApiTimingAdminService timings)
    {
        _timings = timings;
    }

    [HttpGet]
    public async Task<IActionResult> Report([FromQuery] int hours, [FromQuery] string? sort, [FromQuery] int take, CancellationToken ct) =>
        Ok(await _timings.ReportAsync(hours <= 0 ? ApiTimingLimits.DefaultReportHours : hours, sort,
            take <= 0 ? ApiTimingLimits.DefaultReportTake : take, ct));
}
