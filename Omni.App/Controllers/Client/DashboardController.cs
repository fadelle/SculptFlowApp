using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Dashboard;
using Omni.App.Entities.Dtos.Dashboard;
using Omni.App.Entities.Responses.Dashboard;

namespace Omni.App.Controllers.Client;

[ApiController]
[Route("api/dashboard")]
public class DashboardController : DashboardApiController
{
    private readonly IDashboardService _dashboard;

    public DashboardController(IDashboardService dashboard, ICurrentClientContext clientContext) : base(clientContext)
    {
        _dashboard = dashboard;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryResponse>> Summary(
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var summary = await _dashboard.GetSummaryAsync(clientId.Value, from, to, ct);
        return Ok(summary);
    }

    [HttpGet("attention")]
    public async Task<ActionResult<DashboardAttentionResponse>> Attention(CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        return Ok(await _dashboard.GetAttentionAsync(clientId.Value, ct));
    }

    [HttpGet("contacts")]
    public async Task<ActionResult<object>> Contacts(
        [FromQuery] string? stage, [FromQuery] string? search, [FromQuery] string? source, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 1, 200);
        var (items, totalCount) = await _dashboard.GetContactsAsync(clientId.Value, stage, search, source, skip, take, ct);
        return Ok(new { items, totalCount, skip, take });
    }

    [HttpGet("bookings")]
    public async Task<ActionResult<object>> Bookings(
        [FromQuery] string? status, [FromQuery] string? search, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 1, 200);
        var (items, totalCount) = await _dashboard.GetBookingsAsync(clientId.Value, status, search, skip, take, ct);
        return Ok(new { items, totalCount, skip, take });
    }

    [HttpGet("services")]
    public async Task<ActionResult<IReadOnlyList<DashboardServiceRow>>> Services(
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var stats = await _dashboard.GetServiceStatsAsync(clientId.Value, from, to, ct);
        return Ok(stats);
    }
}
