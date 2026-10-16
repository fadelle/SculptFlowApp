using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Appointments;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Requests.Appointments;
using Omni.App.Entities.Responses.Appointments;

namespace Omni.App.Controllers.Client;

[ApiController]
[Route("api/appointments")]
public class AppointmentsController : DashboardApiController
{
    private readonly IAppointmentService _appointments;
    private readonly IAvailabilityService _availability;

    public AppointmentsController(IAppointmentService appointments, IAvailabilityService availability, ICurrentClientContext clientContext) : base(clientContext)
    {
        _appointments = appointments;
        _availability = availability;
    }

    /// <summary>Open slots from the client's structured availability — same calculation the AI's get_available_slots uses.</summary>
    [HttpGet("available")]
    public async Task<ActionResult<AvailabilityResponse>> GetAvailable(
        [FromQuery] Guid? procedureId, [FromQuery] DateOnly? date, [FromQuery] int? days, CancellationToken ct = default)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        return Ok(await _availability.GetSlotsAsync(clientId.Value, procedureId, date, days ?? (date is null ? 7 : 1), ct));
    }

    [HttpPost]
    public async Task<ActionResult<AppointmentResponse>> Create([FromBody] CreateAppointmentRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var appointment = await _appointments.CreateAsync(request with { ClientId = clientId.Value }, ct);
        return CreatedAtAction(nameof(List), null, appointment);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AppointmentResponse>> GetById(Guid id, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var appointment = await _appointments.GetByIdAsync(clientId.Value, id, ct);
        return appointment is null ? NotFound() : Ok(appointment);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<AppointmentResponse>> UpdateStatus(Guid id, [FromBody] UpdateAppointmentStatusRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var appointment = await _appointments.UpdateStatusAsync(clientId.Value, id, request.Status, ct);
        return appointment is null ? NotFound() : Ok(appointment);
    }

    /// <summary>The Appointments page's month calendar. One call returns every appointment in the visible grid
    /// (the requested month plus its leading/trailing days from adjacent months), grouped by local calendar day
    /// in the client's timezone, or the viewer's own (omni-tz cookie) with view=mine — see IAppointmentService.GetCalendarMonthAsync.</summary>
    [HttpGet("calendar")]
    public async Task<ActionResult<CalendarMonthResponse>> GetCalendarMonth(
        [FromQuery] int year, [FromQuery] int month, [FromQuery] string? view = null, CancellationToken ct = default)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        if (month is < 1 or > 12) return BadRequest(new { error = "month must be between 1 and 12." });
        if (year is < 1900 or > 3000) return BadRequest(new { error = "year is out of range." });

        var displayTz = view == "mine" ? ViewerTimeZone.Find(Request.Cookies[ViewerTimeZone.CookieName]) : null;
        var calendar = await _appointments.GetCalendarMonthAsync(clientId.Value, year, month, displayTz, ct);
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
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 1, 200);
        var (items, totalCount) = await _appointments.ListAsync(clientId.Value, status, from, to, skip, take, ct);
        return Ok(new { items, totalCount, skip, take });
    }
}
