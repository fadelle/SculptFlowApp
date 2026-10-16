using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Services.PlatformAdmin;
using Omni.App.Controllers.Filters;

namespace Omni.App.Controllers.Admin;

/// <summary>Platform-admin overview API (internal; called by the Omni.Admin portal's dashboard and Events page).</summary>
[ApiController]
[AllowAnonymous]
[RequirePlatformAdminKey]
[Route("api/platform-admin/overview")]
[ApiErrors]
public class OverviewAdminController : ControllerBase
{
    private readonly IOverviewAdminService _overview;

    public OverviewAdminController(IOverviewAdminService overview)
    {
        _overview = overview;
    }

    [HttpGet("totals")]
    public async Task<IActionResult> Totals(CancellationToken ct) => Ok(await _overview.TotalsAsync(ct));

    [HttpGet("daily-messages")]
    public async Task<IActionResult> DailyMessages([FromQuery] int days, CancellationToken ct) =>
        Ok(await _overview.DailyMessagesAsync(days <= 0 ? 14 : days, ct));

    [HttpGet("problems")]
    public async Task<IActionResult> Problems(CancellationToken ct) => Ok(await _overview.ProblemsAsync(ct));

    [HttpGet("recent-clients")]
    public async Task<IActionResult> RecentClients([FromQuery] int take, CancellationToken ct) =>
        Ok(await _overview.RecentClientsAsync(take <= 0 ? 10 : take, ct));

    [HttpGet("events")]
    public async Task<IActionResult> Events([FromQuery] Guid? clientId, [FromQuery] string? eventType, [FromQuery] int page, CancellationToken ct) =>
        Ok(await _overview.EventsAsync(clientId, eventType, page, ct));

    [HttpGet("event-types")]
    public async Task<IActionResult> EventTypes(CancellationToken ct) => Ok(await _overview.EventTypesAsync(ct));
}
