using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Business.Contracts.Services.PlatformAdmin;
using PlasticSurgery.Controllers.Filters;

namespace PlasticSurgery.Controllers.Admin;

/// <summary>Platform-admin overview API (internal; called by the SculptFlowAdmin portal's dashboard and Events page).</summary>
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

    [HttpGet("recent-clinics")]
    public async Task<IActionResult> RecentClinics([FromQuery] int take, CancellationToken ct) =>
        Ok(await _overview.RecentClinicsAsync(take <= 0 ? 10 : take, ct));

    [HttpGet("events")]
    public async Task<IActionResult> Events([FromQuery] Guid? clinicId, [FromQuery] string? eventType, [FromQuery] int page, CancellationToken ct) =>
        Ok(await _overview.EventsAsync(clinicId, eventType, page, ct));

    [HttpGet("event-types")]
    public async Task<IActionResult> EventTypes(CancellationToken ct) => Ok(await _overview.EventTypesAsync(ct));
}
