using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Controllers;

[ApiController]
[Route("api/appointments")]
public class AppointmentsController : DashboardApiController
{
    private readonly IAppointmentService _appointments;

    public AppointmentsController(IAppointmentService appointments, ICurrentClinicContext clinicContext) : base(clinicContext)
    {
        _appointments = appointments;
    }

    /// <summary>Naive placeholder slot generator — see IAppointmentService.GetAvailableSlotsAsync.</summary>
    [HttpGet("available")]
    public async Task<ActionResult<IReadOnlyList<AvailableSlotResponse>>> GetAvailable(
        [FromQuery] int days = 7, CancellationToken ct = default)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        days = Math.Clamp(days, 1, 30);
        var slots = await _appointments.GetAvailableSlotsAsync(clinicId.Value, days, ct);
        return Ok(slots);
    }

    [HttpPost]
    public async Task<ActionResult<AppointmentResponse>> Create([FromBody] CreateAppointmentRequest request, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        try
        {
            var appointment = await _appointments.CreateAsync(request with { ClinicId = clinicId.Value }, ct);
            return CreatedAtAction(nameof(List), null, appointment);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AppointmentResponse>> GetById(Guid id, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        var appointment = await _appointments.GetByIdAsync(clinicId.Value, id, ct);
        return appointment is null ? NotFound() : Ok(appointment);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<AppointmentResponse>> UpdateStatus(Guid id, [FromBody] UpdateAppointmentStatusRequest request, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        try
        {
            var appointment = await _appointments.UpdateStatusAsync(clinicId.Value, id, request.Status, ct);
            return appointment is null ? NotFound() : Ok(appointment);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<ActionResult<object>> List(
        [FromQuery] string? status,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        take = Math.Clamp(take, 1, 200);
        var (items, totalCount) = await _appointments.ListAsync(clinicId.Value, status, from, to, skip, take, ct);
        return Ok(new { items, totalCount, skip, take });
    }
}
