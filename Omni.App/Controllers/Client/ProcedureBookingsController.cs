using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Procedures;
using Omni.App.Entities.Requests.Procedures;
using Omni.App.Entities.Responses.Procedures;

namespace Omni.App.Controllers.Client;

[ApiController]
[Route("api/procedure-bookings")]
public class ProcedureBookingsController : DashboardApiController
{
    private readonly IProcedureBookingService _bookings;

    public ProcedureBookingsController(IProcedureBookingService bookings, ICurrentClientContext clientContext) : base(clientContext)
    {
        _bookings = bookings;
    }

    [HttpPost]
    public async Task<ActionResult<ProcedureBookingResponse>> Create([FromBody] CreateProcedureBookingRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var booking = await _bookings.CreateAsync(request with { ClientId = clientId.Value }, ct);
        return CreatedAtAction(nameof(List), null, booking);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<ProcedureBookingResponse>> Update(Guid id, [FromBody] UpdateProcedureBookingRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var booking = await _bookings.UpdateAsync(clientId.Value, id, request, ct);
        return booking is null ? NotFound() : Ok(booking);
    }

    [HttpGet]
    public async Task<ActionResult<object>> List(
        [FromQuery] string? status, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 1, 200);
        var (items, totalCount) = await _bookings.ListAsync(clientId.Value, status, skip, take, ct);
        return Ok(new { items, totalCount, skip, take });
    }
}
