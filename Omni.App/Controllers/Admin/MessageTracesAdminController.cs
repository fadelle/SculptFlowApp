using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Services.PlatformAdmin;
using Omni.App.Common.Statics;
using Omni.App.Controllers.Filters;

namespace Omni.App.Controllers.Admin;

/// <summary>
/// Platform-admin message trace (activity.message_trace_steps plus the statuses, charges, events and saved errors of the
/// same messages; called by the Omni.Admin portal's Message traces pages). Read-only.
/// GET ?hours=24&amp;clientId&amp;conversationId&amp;failedOnly&amp;page=1&amp;pageSize=50 — recent traces, latest first.
/// GET {traceId} — one trace's timeline. GET by-message/{messageId} — the timeline of the trace a message was created in.
/// GET resolve?q= — the trace id for a trace id, message id or provider message id. GET summary?hours=24 — dashboard counts.
/// Errors: 400 empty search, 404 nothing found.
/// </summary>
[ApiController]
[AllowAnonymous]
[RequirePlatformAdminKey]
[Route("api/platform-admin/message-traces")]
[ApiErrors]
public class MessageTracesAdminController : ControllerBase
{
    private readonly IMessageTraceAdminService _traces;

    public MessageTracesAdminController(IMessageTraceAdminService traces)
    {
        _traces = traces;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int hours, [FromQuery] Guid? clientId, [FromQuery] Guid? conversationId,
        [FromQuery] bool failedOnly, [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct) =>
        Ok(await _traces.ListAsync(Hours(hours), clientId, conversationId, failedOnly, page <= 0 ? 1 : page,
            pageSize <= 0 ? MessageTraceLimits.DefaultPageSize : pageSize, ct));

    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] int hours, CancellationToken ct) => Ok(await _traces.SummaryAsync(Hours(hours), ct));

    [HttpGet("resolve")]
    public async Task<IActionResult> Resolve([FromQuery] string? q, CancellationToken ct) => Ok(await _traces.ResolveAsync(q ?? string.Empty, ct));

    [HttpGet("by-message/{messageId:guid}")]
    public async Task<IActionResult> ForMessage(Guid messageId, CancellationToken ct) => Ok(await _traces.ForMessageAsync(messageId, ct));

    [HttpGet("{traceId}")]
    public async Task<IActionResult> Get(string traceId, CancellationToken ct) => Ok(await _traces.GetAsync(traceId, ct));

    private static int Hours(int hours) => hours <= 0 ? MessageTraceLimits.DefaultReportHours : hours;
}
