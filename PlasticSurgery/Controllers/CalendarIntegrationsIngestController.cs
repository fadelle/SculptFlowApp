using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Controllers;

/// <summary>Where the dedicated n8n Calendar Sync workflow reports back — the callback counterpart of
/// ICalendarSyncNotifier's two outbound triggers. Server-to-server only (X-Ingest-Key), same trust level as
/// /api/messages/ingest and the Knowledge Benchmark's callback. Every method trusts the id in the URL/body only
/// after confirming it belongs to the clinic the request claims — see CalendarIntegrationService for the checks.</summary>
[ApiController]
[Route("api/calendar-integrations")]
[RequireIngestKey]
public class CalendarIntegrationsIngestController : ControllerBase
{
    private readonly ICalendarIntegrationService _calendar;

    public CalendarIntegrationsIngestController(ICalendarIntegrationService calendar)
    {
        _calendar = calendar;
    }

    /// <summary>n8n's reply to a connect / refresh_calendars trigger.</summary>
    [HttpPost("connect-callback")]
    public async Task<ActionResult> ConnectCallback([FromBody] CalendarConnectCallbackRequest request, CancellationToken ct)
    {
        await _calendar.ApplyConnectCallbackAsync(request, ct);
        return Ok();
    }

    /// <summary>n8n's reply to an appointment create/update/cancel sync trigger.</summary>
    [HttpPost("sync-callback")]
    public async Task<ActionResult> SyncCallback([FromBody] CalendarSyncCallbackRequest request, CancellationToken ct)
    {
        await _calendar.ApplySyncCallbackAsync(request, ct);
        return Ok();
    }
}
