using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Controllers;

[ApiController]
[Route("api/appointments")]
public class AppointmentsController : DashboardApiController
{
    private readonly IAppointmentService _appointments;
    private readonly IAvailabilityService _availability;

    public AppointmentsController(IAppointmentService appointments, IAvailabilityService availability, ICurrentClinicContext clinicContext) : base(clinicContext)
    {
        _appointments = appointments;
        _availability = availability;
    }

    /// <summary>Open slots from the clinic's structured availability — same calculation the AI's get_available_slots uses.</summary>
    [HttpGet("available")]
    public async Task<ActionResult<AvailabilityResponse>> GetAvailable(
        [FromQuery] Guid? procedureId, [FromQuery] DateOnly? date, [FromQuery] int? days, CancellationToken ct = default)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        try
        {
            return Ok(await _availability.GetSlotsAsync(clinicId.Value, procedureId, date, days ?? (date is null ? 7 : 1), ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
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

    /// <summary>The Appointments page's month calendar. One call returns every appointment in the visible grid
    /// (the requested month plus its leading/trailing days from adjacent months), grouped by local calendar day
    /// in the clinic's own timezone — see IAppointmentService.GetCalendarMonthAsync.</summary>
    [HttpGet("calendar")]
    public async Task<ActionResult<CalendarMonthResponse>> GetCalendarMonth(
        [FromQuery] int year, [FromQuery] int month, CancellationToken ct = default)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        if (month is < 1 or > 12) return BadRequest(new { error = "month must be between 1 and 12." });
        if (year is < 1900 or > 3000) return BadRequest(new { error = "year is out of range." });

        var calendar = await _appointments.GetCalendarMonthAsync(clinicId.Value, year, month, ct);
        return Ok(calendar);
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
