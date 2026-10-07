using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Business.Contracts.Services.Calendars;
using PlasticSurgery.Controllers.Filters;
using PlasticSurgery.Entities.Requests.Calendars;

namespace PlasticSurgery.Controllers.Integrations;

/// <summary>Where the dedicated n8n Calendar Sync workflow reports back — the callback counterpart of
/// ICalendarSyncNotifier's one outbound trigger. Server-to-server only (X-Ingest-Key), same trust level as
/// /api/messages/ingest and the Knowledge Benchmark's callback. Connect/disconnect no longer involve n8n at all
/// (see CalendarOAuthController) — this now only ever handles the appointment sync callback. Trusts the id in the
/// body only after confirming it belongs to the clinic the request claims — see CalendarIntegrationService for the
/// checks.</summary>
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

    /// <summary>n8n's reply to an appointment create/update/cancel sync trigger.</summary>
    [HttpPost("sync-callback")]
    public async Task<ActionResult> SyncCallback([FromBody] CalendarSyncCallbackRequest request, CancellationToken ct)
    {
        await _calendar.ApplySyncCallbackAsync(request, ct);
        return Ok();
    }
}
