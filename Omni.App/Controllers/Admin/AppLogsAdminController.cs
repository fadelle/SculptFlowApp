using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Services.PlatformAdmin;
using Omni.App.Common.Statics;
using Omni.App.Controllers.Filters;

namespace Omni.App.Controllers.Admin;

/// <summary>
/// Platform-admin error log (activity.app_logs; called by the Omni.Admin portal's Errors page). Read-only.
/// GET groups?hours=24&amp;level=warning&amp;clinicId&amp;search&amp;sort=recent&amp;take=100 — one row per problem.
/// GET ?hours&amp;level&amp;clinicId&amp;search&amp;fingerprint&amp;traceId&amp;page=1&amp;pageSize=50 — single rows, newest first.
/// GET {id} — one row in full. GET summary?hours=24 — counts per level (Overview tile).
/// level: warning | error | critical (a minimum). sort: recent | count. Errors: 400 unknown level/sort, 404 unknown id.
/// </summary>
[ApiController]
[AllowAnonymous]
[RequirePlatformAdminKey]
[Route("api/platform-admin/logs")]
[ApiErrors]
public class AppLogsAdminController : ControllerBase
{
    private readonly IAppLogAdminService _logs;

    public AppLogsAdminController(IAppLogAdminService logs)
    {
        _logs = logs;
    }

    [HttpGet("groups")]
    public async Task<IActionResult> Groups([FromQuery] int hours, [FromQuery] string? level, [FromQuery] Guid? clinicId,
        [FromQuery] string? search, [FromQuery] string? sort, [FromQuery] int take, CancellationToken ct) =>
        Ok(await _logs.GroupsAsync(Hours(hours), level, clinicId, search, sort,
            take <= 0 ? AppLogLimits.DefaultGroupTake : take, ct));

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int hours, [FromQuery] string? level, [FromQuery] Guid? clinicId,
        [FromQuery] string? search, [FromQuery] string? fingerprint, [FromQuery] string? traceId, [FromQuery] int page,
        [FromQuery] int pageSize, CancellationToken ct) =>
        Ok(await _logs.ListAsync(Hours(hours), level, clinicId, search, fingerprint, traceId, page <= 0 ? 1 : page,
            pageSize <= 0 ? AppLogLimits.DefaultPageSize : pageSize, ct));

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct) => Ok(await _logs.GetAsync(id, ct));

    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] int hours, CancellationToken ct) =>
        Ok(await _logs.SummaryAsync(Hours(hours), ct));

    private static int Hours(int hours) => hours <= 0 ? AppLogLimits.DefaultReportHours : hours;
}
