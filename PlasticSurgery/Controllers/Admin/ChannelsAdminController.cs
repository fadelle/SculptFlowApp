using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Business.Contracts.Services.PlatformAdmin;
using PlasticSurgery.Controllers.Filters;
using PlasticSurgery.Entities.Requests.PlatformAdmin;

namespace PlasticSurgery.Controllers.Admin;

/// <summary>
/// Platform-admin channels API (internal; called by the SculptFlowAdmin portal): messaging connections, calendar and
/// TikTok connections. Writes answer { clinicId }. Errors: 400 invalid input, 404 not found, 422 not allowed now,
/// 502/503 provider unavailable.
/// </summary>
[ApiController]
[AllowAnonymous]
[RequirePlatformAdminKey]
[Route("api/platform-admin/channels")]
[ApiErrors]
public class ChannelsAdminController : ControllerBase
{
    private readonly IChannelAdminService _channels;

    public ChannelsAdminController(IChannelAdminService channels)
    {
        _channels = channels;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? clinicId, [FromQuery] string? channel, [FromQuery] bool problemsOnly,
        CancellationToken ct) =>
        Ok(await _channels.ListChannelsAsync(clinicId, channel, problemsOnly, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        await _channels.GetChannelAsync(id, ct) is { } row ? Ok(row) : NotFound();

    [HttpGet("{id:guid}/health-events")]
    public async Task<IActionResult> HealthEvents(Guid id, CancellationToken ct) => Ok(await _channels.HealthEventsAsync(id, ct));

    [HttpPost("{id:guid}/disconnect")]
    public async Task<IActionResult> Disconnect(Guid id, CancellationToken ct) => Ok(await _channels.DisconnectAsync(id, ct));

    [HttpPost("whatsapp/infobip/{clinicId:guid}")]
    public async Task<IActionResult> ConnectInfobip(Guid clinicId, [FromBody] InfobipSenderBody request, CancellationToken ct) =>
        Ok(await _channels.ConnectInfobipSenderAsync(clinicId, request.Sender, ct));

    [HttpGet("calendars")]
    public async Task<IActionResult> Calendars([FromQuery] Guid? clinicId, CancellationToken ct) => Ok(await _channels.ListCalendarsAsync(clinicId, ct));

    [HttpPost("calendars/{id:guid}/disconnect")]
    public async Task<IActionResult> DisconnectCalendar(Guid id, CancellationToken ct) => Ok(await _channels.DisconnectCalendarAsync(id, ct));

    [HttpPut("calendars/{id:guid}/sync")]
    public async Task<IActionResult> SetCalendarSync(Guid id, [FromBody] FlagBody request, CancellationToken ct) =>
        Ok(await _channels.SetCalendarSyncAsync(id, request.Value, ct));

    [HttpGet("tiktok")]
    public async Task<IActionResult> TikTok([FromQuery] Guid? clinicId, CancellationToken ct) => Ok(await _channels.ListTikTokAsync(clinicId, ct));

    [HttpPost("tiktok/{id:guid}/disconnect")]
    public async Task<IActionResult> DisconnectTikTok(Guid id, CancellationToken ct) => Ok(await _channels.DisconnectTikTokAsync(id, ct));
}
