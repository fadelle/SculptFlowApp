using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Calendars;
using Omni.App.Entities.Requests.Calendars;
using Omni.App.Entities.Responses.Calendars;

namespace Omni.App.Controllers.Client;

/// <summary>Settings → Calendar Integrations' API. clientId always from CurrentClientContext, never the client —
/// same rule as every other dashboard controller. See ICalendarIntegrationService for the full design. Connect
/// itself is NOT here — a JSON POST can't do a full-page redirect to Google/Microsoft's consent screen, so that one
/// lives on CalendarOAuthController as a plain GET route instead; the actual Razor Page UI doesn't call this
/// controller at all (it uses its own OnPost* handlers), but it's kept as the API surface for the feature.</summary>
[ApiController]
[Route("api/calendar-integrations")]
public class CalendarIntegrationsController : DashboardApiController
{
    private readonly ICalendarIntegrationService _calendar;

    public CalendarIntegrationsController(ICalendarIntegrationService calendar, ICurrentClientContext clientContext) : base(clientContext)
    {
        _calendar = calendar;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CalendarIntegrationResponse>>> List(CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();
        return Ok(await _calendar.ListAsync(clientId.Value, ct));
    }

    [HttpPost("{provider}/refresh-calendars")]
    public async Task<ActionResult> RefreshCalendars(string provider, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();
        try { await _calendar.RequestRefreshCalendarsAsync(clientId.Value, provider, ct); return NoContent(); }
        // 400 for both here (not the usual 422): a refresh the provider can't do yet is the caller's to fix.
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("{provider}/select-calendar")]
    public async Task<ActionResult<CalendarIntegrationResponse>> SelectCalendar(
        string provider, [FromBody] SelectCalendarRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();
        return Ok(await _calendar.SelectCalendarAsync(clientId.Value, provider, request.ExternalCalendarId, ct));
    }

    [HttpPost("{provider}/sync-enabled")]
    public async Task<ActionResult<CalendarIntegrationResponse>> SetSyncEnabled(
        string provider, [FromBody] SetCalendarSyncEnabledRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();
        return Ok(await _calendar.SetSyncEnabledAsync(clientId.Value, provider, request.Enabled, ct));
    }

    [HttpPost("{provider}/disconnect")]
    public async Task<ActionResult> Disconnect(string provider, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();
        await _calendar.DisconnectAsync(clientId.Value, provider, ct);
        return NoContent();
    }
}
