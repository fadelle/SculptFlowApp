using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController : DashboardApiController
{
    private readonly IDashboardService _dashboard;

    public DashboardController(IDashboardService dashboard, ICurrentClinicContext clinicContext) : base(clinicContext)
    {
        _dashboard = dashboard;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryResponse>> Summary(CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        var summary = await _dashboard.GetSummaryAsync(clinicId.Value, ct);
        return Ok(summary);
    }

    [HttpGet("leads")]
    public async Task<ActionResult<object>> Leads(
        [FromQuery] string? status, [FromQuery] string? search, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        take = Math.Clamp(take, 1, 200);
        var (items, totalCount) = await _dashboard.GetLeadsAsync(clinicId.Value, status, search, skip, take, ct);
        return Ok(new { items, totalCount, skip, take });
    }

    [HttpGet("appointments")]
    public async Task<ActionResult<object>> Appointments(
        [FromQuery] string? status, [FromQuery] string? search, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        take = Math.Clamp(take, 1, 200);
        var (items, totalCount) = await _dashboard.GetAppointmentsAsync(clinicId.Value, status, search, skip, take, ct);
        return Ok(new { items, totalCount, skip, take });
    }

    [HttpGet("procedures")]
    public async Task<ActionResult<IReadOnlyList<DashboardProcedureRow>>> Procedures(CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        var stats = await _dashboard.GetProcedureStatsAsync(clinicId.Value, ct);
        return Ok(stats);
    }
}
