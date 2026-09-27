using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Controllers;

/// <summary>Settings → Calendar Integrations' API. clinicId always from CurrentClinicContext, never the client —
/// same rule as every other dashboard controller. See ICalendarIntegrationService for the full design.</summary>
[ApiController]
[Route("api/calendar-integrations")]
public class CalendarIntegrationsController : DashboardApiController
{
    private readonly ICalendarIntegrationService _calendar;

    public CalendarIntegrationsController(ICalendarIntegrationService calendar, ICurrentClinicContext clinicContext) : base(clinicContext)
    {
        _calendar = calendar;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CalendarIntegrationResponse>>> List(CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        return Ok(await _calendar.ListAsync(clinicId.Value, ct));
    }

    [HttpPost("{provider}/connect")]
    public async Task<ActionResult<CalendarIntegrationResponse>> Connect(string provider, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        try { return Ok(await _calendar.RequestConnectAsync(clinicId.Value, provider, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("{provider}/refresh-calendars")]
    public async Task<ActionResult> RefreshCalendars(string provider, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        try { await _calendar.RequestRefreshCalendarsAsync(clinicId.Value, provider, ct); return NoContent(); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("{provider}/select-calendar")]
    public async Task<ActionResult<CalendarIntegrationResponse>> SelectCalendar(
        string provider, [FromBody] SelectCalendarRequest request, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        try { return Ok(await _calendar.SelectCalendarAsync(clinicId.Value, provider, request.ExternalCalendarId, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("{provider}/sync-enabled")]
    public async Task<ActionResult<CalendarIntegrationResponse>> SetSyncEnabled(
        string provider, [FromBody] SetCalendarSyncEnabledRequest request, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        try { return Ok(await _calendar.SetSyncEnabledAsync(clinicId.Value, provider, request.Enabled, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("{provider}/disconnect")]
    public async Task<ActionResult> Disconnect(string provider, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        await _calendar.DisconnectAsync(clinicId.Value, provider, ct);
        return NoContent();
    }
}
