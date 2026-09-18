using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Controllers;

[ApiController]
[Route("api/procedure-bookings")]
public class ProcedureBookingsController : DashboardApiController
{
    private readonly IProcedureBookingService _bookings;

    public ProcedureBookingsController(IProcedureBookingService bookings, ICurrentClinicContext clinicContext) : base(clinicContext)
    {
        _bookings = bookings;
    }

    [HttpPost]
    public async Task<ActionResult<ProcedureBookingResponse>> Create([FromBody] CreateProcedureBookingRequest request, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        var booking = await _bookings.CreateAsync(request with { ClinicId = clinicId.Value }, ct);
        return CreatedAtAction(nameof(List), null, booking);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<ProcedureBookingResponse>> Update(Guid id, [FromBody] UpdateProcedureBookingRequest request, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        try
        {
            var booking = await _bookings.UpdateAsync(clinicId.Value, id, request, ct);
            return booking is null ? NotFound() : Ok(booking);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<ActionResult<object>> List(
        [FromQuery] string? status, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        take = Math.Clamp(take, 1, 200);
        var (items, totalCount) = await _bookings.ListAsync(clinicId.Value, status, skip, take, ct);
        return Ok(new { items, totalCount, skip, take });
    }
}
