using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Services.Ai;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Controllers.Filters;
using Omni.App.Entities.Dtos.Ai;
using Omni.App.Entities.Dtos.Bookings;
using Omni.App.Entities.Requests.Ai;
using Omni.App.Entities.Requests.Knowledge;
using Omni.App.Entities.Requests.Contacts;
using Omni.App.Entities.Responses.Ai;
using Omni.App.Entities.Responses.Bookings;
using Omni.App.Entities.Responses.Knowledge;
using Omni.App.Entities.Responses.Contacts;
using Omni.App.Entities.Responses.Catalog;

namespace Omni.App.Controllers.Integrations;

/// <summary>
/// The AI bot's (n8n) tool surface — one trusted entry point per tool the AI can call.
/// Protected the same way as /api/messages/ingest (RequireIngestKey): this is server-to-server
/// traffic from n8n, not something the browser should be able to call directly. Every action still
/// takes clientId explicitly and scopes through it — same multi-client rule as the rest of the API.
///
/// Every action is a [BotTool]: the call names its bot (X-Omni-Bot-Id, from the AI trigger) and Omni allows only that bot's
/// actions (BotCapability) on its own client's data, whatever the prompt said; a refused call answers success = false,
/// code TOOL_NOT_ALLOWED. A call from a bot test conversation (X-Omni-Test-Session) reads real data but simulates every
/// write (IBotTestService): no booking, contact change, handoff or provider message is real.
///
/// Deliberately narrower than the dashboard's own endpoints where it matters: update_contact can't
/// touch identity fields (name/phone/email), and schedule/cancel/handoff each log a dedicated
/// event with the AI as the source so members can see what the AI did and why (see EventTypes).
///
/// The tools themselves live in IBotToolService (which tool is for what: BotToolService); each action only hands its
/// arguments and the [BotTool] run over and answers the tool's status and body. The routes, capabilities and features
/// here match BotTools.
/// </summary>
[ApiController]
[Route("api/ai")]
[RequireIngestKey]
[ApiErrors]
[TraceBotTool]
public class AiController : ControllerBase
{
    private readonly IBotToolService _tools;

    public AiController(IBotToolService tools)
    {
        _tools = tools;
    }

    private BotRunContext Run => BotToolAttribute.RunOf(HttpContext);

    /// <summary>search_knowledge — semantic search over the client's approved Knowledge Base, limited to the bot's sources.
    /// For INFORMATION questions only; the AI supplies only the query text. Returns relevant chunks (possibly none), never a
    /// composed answer; 503 when the embeddings provider fails.</summary>
    [HttpPost("knowledge/search")]
    [BotTool(BotCapability.Knowledge)]
    public async Task<ActionResult<KnowledgeSearchResponse>> SearchKnowledge([FromBody] KnowledgeSearchRequest request, CancellationToken ct) =>
        Answer(await _tools.SearchKnowledgeAsync(Run, request, ct));

    /// <summary>get_client_info — hours, location, contact info, booking rules.</summary>
    [HttpGet("client-info")]
    [BotTool(BotCapability.Knowledge)]
    public async Task<ActionResult<ClientInfoResponse>> GetClientInfo([FromQuery] Guid clientId, CancellationToken ct) =>
        Answer(await _tools.GetClientInfoAsync(Run, clientId, ct));

    /// <summary>get_services — the structured, authoritative (active) service records needed for booking.</summary>
    [HttpGet("services")]
    [BotTool(BotCapability.Services)]
    public async Task<ActionResult<IReadOnlyList<ServiceResponse>>> GetServices([FromQuery] Guid clientId, CancellationToken ct = default) =>
        Answer(await _tools.GetServicesAsync(Run, clientId, ct));

    /// <summary>get_contact_context — what's already known about the person the AI is talking to.</summary>
    [HttpGet("contacts/{contactId:guid}")]
    [BotTool]
    public async Task<ActionResult<ContactResponse>> GetContactContext(Guid contactId, [FromQuery] Guid clientId, CancellationToken ct) =>
        Answer(await _tools.GetContactContextAsync(Run, clientId, contactId, ct));

    /// <summary>update_contact (language), update_contact_details and update_stage share this route; each field needs its own
    /// capability. Cannot change identity fields (name/phone/email).</summary>
    [HttpPatch("contacts/{contactId:guid}")]
    [BotTool]
    public async Task<ActionResult<ContactResponse>> UpdateContact(
        Guid contactId, [FromQuery] Guid clientId, [FromBody] UpdateContactContextRequest request, CancellationToken ct) =>
        Answer(await _tools.UpdateContactAsync(Run, clientId, contactId, request, ct));

    /// <summary>get_available_slots — real open booking slots in the client's timezone. `date` is a local "yyyy-MM-dd"
    /// (default today); `days` (1-14, default 1 when a date is given, else 7).</summary>
    [HttpGet("bookings/available")]
    [BotTool(BotCapability.Availability)]
    [RequireFeature(EntitlementKeys.Bookings, AiTool = true)]
    public async Task<ActionResult<AvailabilityResponse>> GetAvailableSlots(
        [FromQuery] Guid clientId, [FromQuery] Guid? contactId, [FromQuery] Guid? serviceId, [FromQuery] DateOnly? date,
        [FromQuery] int? days, CancellationToken ct = default) =>
        Answer(await _tools.GetAvailableSlotsAsync(Run, clientId, contactId, serviceId, date, days, ct));

    /// <summary>schedule_booking — the ONE tool the AI uses to book or move a booking; the backend decides which. Always HTTP
    /// 200 for business outcomes.</summary>
    [HttpPost("bookings/schedule")]
    [BotTool(BotCapability.Bookings)]
    [RequireFeature(EntitlementKeys.Bookings, AiTool = true)]
    public async Task<ActionResult<ScheduleBookingResult>> ScheduleBooking(
        [FromQuery] Guid clientId, [FromQuery] Guid contactId, [FromBody] ScheduleBookingRequest request, CancellationToken ct) =>
        Answer(await _tools.ScheduleBookingAsync(Run, clientId, contactId, request, ct));

    /// <summary>get_my_bookings — this contact's upcoming booked/confirmed bookings (soonest first).</summary>
    [HttpGet("bookings/upcoming")]
    [BotTool(BotCapability.Bookings)]
    [RequireFeature(EntitlementKeys.Bookings, AiTool = true)]
    public async Task<ActionResult<UpcomingBookingsResponse>> GetMyBookings(
        [FromQuery] Guid clientId, [FromQuery] Guid contactId, CancellationToken ct) =>
        Answer(await _tools.GetMyBookingsAsync(Run, clientId, contactId, ct));

    /// <summary>cancel_booking — the contact cancels THEIR upcoming booking. Always HTTP 200 for business outcomes.</summary>
    [HttpPost("bookings/cancel")]
    [BotTool(BotCapability.Bookings)]
    [RequireFeature(EntitlementKeys.Bookings, AiTool = true)]
    public async Task<ActionResult<CancelBookingResult>> CancelBooking(
        [FromQuery] Guid clientId, [FromQuery] Guid contactId, [FromQuery] Guid? bookingId,
        [FromBody] CancelBookingRequest request, CancellationToken ct) =>
        Answer(await _tools.CancelBookingAsync(Run, clientId, contactId, bookingId, request, ct));

    /// <summary>reply — the bot's message to the contact; 409 conversation_in_human_mode / ai_reply_refused.</summary>
    [HttpPost("conversations/{id:guid}/reply")]
    [BotTool]
    public async Task<ActionResult<BotReplyOutcome>> Reply(Guid id, [FromQuery] Guid clientId, [FromBody] AiReplyRequest request, CancellationToken ct) =>
        Answer(await _tools.ReplyAsync(Run, id, clientId, request, ct));

    /// <summary>handoff_to_human — flips the conversation to human mode, attributed to the AI with the reason recorded.</summary>
    [HttpPost("conversations/{id:guid}/handoff")]
    [BotTool]
    public async Task<ActionResult<object>> HandoffToHuman(
        Guid id, [FromQuery] Guid clientId, [FromBody] HandoffToHumanRequest request, CancellationToken ct) =>
        Answer(await _tools.HandoffAsync(Run, id, clientId, request, ct));

    // The tool's status and body as this endpoint's answer (a 404 without a body is an empty one).
    private static ActionResult Answer(BotToolResult result) => result.Status switch
    {
        StatusCodes.Status200OK => new OkObjectResult(result.Body),
        StatusCodes.Status400BadRequest => new BadRequestObjectResult(result.Body),
        StatusCodes.Status404NotFound => result.Body is null ? new NotFoundResult() : new NotFoundObjectResult(result.Body),
        StatusCodes.Status409Conflict => new ConflictObjectResult(result.Body),
        _ => new ObjectResult(result.Body) { StatusCode = result.Status }
    };
}
