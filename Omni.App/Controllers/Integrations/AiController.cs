using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Ai;
using Omni.App.Business.Contracts.Services.Bookings;
using Omni.App.Business.Contracts.Services.Knowledge;
using Omni.App.Business.Contracts.Services.Contacts;
using Omni.App.Business.Contracts.Services.Catalog;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
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
/// The AI bot's (n8n) tool surface — one trusted entry point per tool the AI can call, all
/// delegating to the same services the dashboard/API already use (no duplicated business logic).
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
/// WHICH TOOL FOR WHAT — rule of thumb: the Knowledge Base is for INFORMATION; the structured tools
/// are for LIVE DATA and ACTIONS. The Knowledge Base is never the source of truth for contact data,
/// booking availability, bookings, conversation state or handoff.
///
///   search_knowledge       → client-approved informational content (only the bot's sources): hours, address, policies,
///                            pricing, services, preparation, recovery, FAQs. (knowledge)
///   get_services           → the authoritative structured service records. (services)
///   get_contact_context    → what is currently known about THIS contact. (every bot)
///   update_contact         → the contact's language (every bot); update_contact_details → city, timeline, notes,
///                            follow-up, qualification (contact_update); update_stage → the pipeline stage (pipeline).
///   get_available_slots    → LIVE booking availability. (availability)
///   get_my_bookings, schedule_booking, cancel_booking → the contact's own bookings. (bookings)
///   reply, handoff_to_human → send the bot's reply / switch the conversation to the team. (every bot)
/// </summary>
[ApiController]
[Route("api/ai")]
[RequireIngestKey]
[ApiErrors]
[TraceBotTool]
public class AiController : ControllerBase
{
    private readonly IClientContext _clientContext;
    private readonly ICatalogService _services;
    private readonly IContactService _contacts;
    private readonly IBookingService _bookings;
    private readonly IAvailabilityService _availability;
    private readonly IKnowledgeSearchService _knowledgeSearch;
    private readonly IBotRuntimeService _runtime;
    private readonly IBotTestService _tests;
    private readonly IBotConversationService _botConversations;

    public AiController(
        IClientContext clientContext, ICatalogService services, IContactService contacts,
        IBookingService bookings, IAvailabilityService availability, IKnowledgeSearchService knowledgeSearch,
        IBotRuntimeService runtime, IBotTestService tests, IBotConversationService botConversations)
    {
        _clientContext = clientContext;
        _services = services;
        _contacts = contacts;
        _bookings = bookings;
        _availability = availability;
        _knowledgeSearch = knowledgeSearch;
        _runtime = runtime;
        _tests = tests;
        _botConversations = botConversations;
    }

    private BotRunContext Run => BotToolAttribute.RunOf(HttpContext);

    /// <summary>search_knowledge — semantic search over the client's approved Knowledge Base, limited to the bot's sources.
    /// For INFORMATION questions only (hours, address, policies, pricing, doctors, service
    /// explanations, recovery, FAQs) — not contact data, availability, bookings or handoff. clientId is the
    /// one n8n already has in context; the AI supplies only the query text. Returns relevant chunks
    /// (possibly none) — never a composed answer.</summary>
    [HttpPost("knowledge/search")]
    [BotTool(BotCapability.Knowledge)]
    public async Task<ActionResult<KnowledgeSearchResponse>> SearchKnowledge([FromBody] KnowledgeSearchRequest request, CancellationToken ct)
    {
        if (request.ClientId == Guid.Empty || string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest(new { error = "clientId and query are required." });
        }

        try
        {
            return Ok(await _knowledgeSearch.SearchAsync(request.ClientId, request.Query, request.Limit, Run.KnowledgeFilter, ct));
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }

    /// <summary>get_client_info — hours, location, contact info, booking rules. Partly overlaps the
    /// Knowledge Base (search_knowledge), which is now the preferred place for this kind of
    /// information; kept working for now and may be retired later.</summary>
    [HttpGet("client-info")]
    [BotTool(BotCapability.Knowledge)]
    public async Task<ActionResult<ClientInfoResponse>> GetClientInfo([FromQuery] Guid clientId, CancellationToken ct)
    {
        var client = await _clientContext.GetByIdAsync(clientId, ct);
        if (client is null) return NotFound();

        return Ok(new ClientInfoResponse(
            client.Id, client.Name, client.Phone, client.Email, client.Website,
            client.Address, client.OperatingHours, client.BookingInfo, client.Timezone));
    }

    /// <summary>get_services — the structured, authoritative service records (id, name, active flag,
    /// booking duration) needed for booking. Use search_knowledge to EXPLAIN a service;
    /// use this when an actual service id/record is needed.</summary>
    [HttpGet("services")]
    [BotTool(BotCapability.Services)]
    public async Task<ActionResult<IReadOnlyList<ServiceResponse>>> GetServices(
        [FromQuery] Guid clientId, CancellationToken ct = default)
    {
        // Always ACTIVE services only — the AI must never see (and so never suggest or book) an
        // inactive one. Historical use of inactive services (existing contacts/bookings/bookings,
        // Campaign filtering) goes through the normal backend queries, not this tool. There is
        // deliberately no activeOnly switch here; a stray activeOnly=false query param is ignored.
        var services = await _services.ListAsync(clientId, activeOnly: true, ct);
        return Ok(services);
    }

    /// <summary>get_contact_context — what's already known about the person the AI is talking to. A test run has no real
    /// contact.</summary>
    [HttpGet("contacts/{contactId:guid}")]
    [BotTool]
    public async Task<ActionResult<ContactResponse>> GetContactContext(Guid contactId, [FromQuery] Guid clientId, CancellationToken ct)
    {
        if (Run.IsTest) return Ok(new { id = contactId, simulated = true, fullName = "Test contact" }); // text-ok: read by the AI only
        var contact = await _contacts.GetByIdAsync(clientId, contactId, ct);
        return contact is null ? NotFound() : Ok(contact);
    }

    /// <summary>update_contact (language), update_contact_details (service interest, city, timeline, notes, follow-up,
    /// qualification) and update_stage share this route; each needs its own capability (IBotRuntimeService.CheckContactUpdate).
    /// Cannot change identity fields (name/phone/email) — those come from the contact's actual source.</summary>
    [HttpPatch("contacts/{contactId:guid}")]
    [BotTool]
    public async Task<ActionResult<ContactResponse>> UpdateContact(
        Guid contactId, [FromQuery] Guid clientId, [FromBody] UpdateContactContextRequest request, CancellationToken ct)
    {
        var (clean, missing) = _runtime.CheckContactUpdate(Run, request);
        if (missing is not null) return Ok(BotToolAttribute.NotAllowed(BotToolCheck.NotAllowed));
        if (Run.IsTest)
        {
            await _tests.NoteContactUpdateAsync(Run, clean, ct);
            return Ok(new { success = true, simulated = true });
        }
        var contact = await _contacts.UpdateContextAsync(clientId, contactId, clean, ct);
        return contact is null ? NotFound() : Ok(contact);
    }

    /// <summary>get_available_slots — real open booking slots from the client's structured availability (weekly schedule,
    /// date exceptions, booking rules, service duration, existing bookings), in the client's timezone. `date` is a local
    /// "yyyy-MM-dd" (default today); `days` (1-14, default 1 when a date is given, else 7) searches that many days from it.
    /// Never computed by the AI. A client that hasn't configured availability returns configured=false and no slots.</summary>
    [HttpGet("bookings/available")]
    [BotTool(BotCapability.Availability)]
    [RequireFeature(EntitlementKeys.Bookings, AiTool = true)]
    public async Task<ActionResult<AvailabilityResponse>> GetAvailableSlots(
        [FromQuery] Guid clientId, [FromQuery] Guid? contactId, [FromQuery] Guid? serviceId, [FromQuery] DateOnly? date,
        [FromQuery] int? days, CancellationToken ct = default)
    {
        // With contactId (always passed by the n8n workflow, never chosen by the AI) the reply also carries the contact's own
        // upcoming bookings and the backend's canCreateNewBooking decision — kept separate from the requested date and slots.
        // Read-only: this endpoint never books, reschedules or cancels. A test run's contact is the test session's.
        ContactBookingContext? context = null;
        if (Run.IsTest)
        {
            context = await _tests.GetBookingContextAsync(Run, ct);
        }
        else if (contactId is { } contact)
        {
            context = await _bookings.GetBookingContextAsync(clientId, contact, ct);
            if (context is null) return NotFound(new { error = "Contact not found for this client." });
        }

        var result = await _availability.GetSlotsAsync(clientId, serviceId, date, days ?? (date is null ? 7 : 1), ct);
        if (context is null) return Ok(result);

        return Ok(result with
        {
            RequestedDate = date?.ToString("yyyy-MM-dd") ?? result.Today,
            HasUpcomingBooking = context.HasUpcomingBooking,
            CanCreateNewBooking = context.CanCreateNewBooking,
            BookingBlockReason = context.BookingBlockReason,
            ExistingUpcomingBookings = context.ExistingUpcomingBookings
        });
    }

    /// <summary>schedule_booking — the ONE tool the AI uses to book or move a booking. The BACKEND decides which from the real
    /// booking state: none upcoming → book; one upcoming AND confirmReplaceExisting=true → reschedule it; upcoming without consent →
    /// nothing changes and CONFIRMATION_REQUIRED comes back. clientId/contactId are workflow values, never AI-chosen. Always HTTP 200 for
    /// business outcomes; only Success=true with Operation created/rescheduled means the contact's booking changed.</summary>
    [HttpPost("bookings/schedule")]
    [BotTool(BotCapability.Bookings)]
    [RequireFeature(EntitlementKeys.Bookings, AiTool = true)]
    public async Task<ActionResult<ScheduleBookingResult>> ScheduleBooking(
        [FromQuery] Guid clientId, [FromQuery] Guid contactId, [FromBody] ScheduleBookingRequest request, CancellationToken ct)
    {
        if (Run.IsTest) return Ok(await _tests.ScheduleAsync(Run, request, ct));

        var o = await _bookings.ScheduleAsync(clientId, contactId, request, ct);
        var instruction = AiResultInstructions.ForSchedule(o.Code, o.Existing?.FirstOrDefault()?.Label, o.RequestedLabel);

        return Ok(new ScheduleBookingResult(
            o.Code is "BOOKED" or "RESCHEDULED", o.Operation, o.Code, o.Message, instruction,
            o.Booking, o.PreviousBooking,
            o.Code is "BOOKED" or "RESCHEDULED" ? null : o.Existing,
            o.Code is "BOOKED" or "RESCHEDULED" ? null : o.RequestedLabel));
    }

    /// <summary>get_my_bookings — this contact's upcoming booked/confirmed bookings (soonest first), in client-local
    /// wording. Use it before rescheduling/cancelling (to get the booking id) and to answer "what do I have booked?".
    /// contactId is the current conversation's contact, supplied by the workflow — not chosen by the AI.</summary>
    [HttpGet("bookings/upcoming")]
    [BotTool(BotCapability.Bookings)]
    [RequireFeature(EntitlementKeys.Bookings, AiTool = true)]
    public async Task<ActionResult<UpcomingBookingsResponse>> GetMyBookings(
        [FromQuery] Guid clientId, [FromQuery] Guid contactId, CancellationToken ct)
    {
        if (Run.IsTest) return Ok(await _tests.GetUpcomingAsync(Run, ct));
        return Ok(await _bookings.GetUpcomingForContactAsync(clientId, contactId, ct));
    }

    /// <summary>cancel_booking — contact asks to cancel THEIR upcoming booking. No booking id is needed for a contact with one
    /// upcoming booking. Always HTTP 200 for business outcomes; only Success=true means something was canceled. The AI must have
    /// the contact's explicit confirmation before calling it.</summary>
    [HttpPost("bookings/cancel")]
    [BotTool(BotCapability.Bookings)]
    [RequireFeature(EntitlementKeys.Bookings, AiTool = true)]
    public async Task<ActionResult<CancelBookingResult>> CancelBooking(
        [FromQuery] Guid clientId, [FromQuery] Guid contactId, [FromQuery] Guid? bookingId,
        [FromBody] CancelBookingRequest request, CancellationToken ct)
    {
        if (Run.IsTest) return Ok(await _tests.CancelAsync(Run, ct));

        var o = await _bookings.CancelBookingAsync(clientId, contactId, bookingId, request.Reason, ct);
        var instruction = AiResultInstructions.ForCancel(o.Code);

        return Ok(new CancelBookingResult(
            o.Code == "CANCELED", o.Operation, o.Code, o.Message, instruction, o.Booking, o.Existing));
    }

    /// <summary>reply — the bot's message to the contact. The conversation must still be in AI mode (a member may have taken
    /// over while the AI was thinking): 409 conversation_in_human_mode otherwise; 409 ai_reply_refused when billing refused
    /// the reply (the conversation is with the team then). Never changes the mode.</summary>
    [HttpPost("conversations/{id:guid}/reply")]
    [BotTool]
    public async Task<ActionResult<BotReplyOutcome>> Reply(Guid id, [FromQuery] Guid clientId, [FromBody] AiReplyRequest request, CancellationToken ct)
    {
        // Caught here: the workflow needs these two outcomes as 409s with a code (it stops without retrying), not as 422s.
        try
        {
            var outcome = await _botConversations.ReplyAsync(Run, id, request.Content, ct);
            return outcome is null ? NotFound() : Ok(outcome);
        }
        catch (ConversationNotInAiModeException ex)
        {
            return Conflict(new { sent = false, code = "conversation_in_human_mode", conversationId = id, mode = ex.CurrentMode });
        }
        catch (AiReplyRefusedException ex)
        {
            return Conflict(new { sent = false, code = "ai_reply_refused", reason = ex.Reason, conversationId = id, mode = ConversationMode.Human });
        }
    }

    /// <summary>handoff_to_human — the contact asks for a person, the bot can't help, an upset customer, etc. Flips the
    /// conversation to human mode, same as the dashboard's Take Over, but attributed to the AI with the reason recorded on the
    /// event, and sends the bot's handoff message once (only on the real change).</summary>
    [HttpPost("conversations/{id:guid}/handoff")]
    [BotTool]
    public async Task<ActionResult<object>> HandoffToHuman(
        Guid id, [FromQuery] Guid clientId, [FromBody] HandoffToHumanRequest request, CancellationToken ct)
    {
        var handedOff = await _botConversations.HandOffAsync(Run, id, request.Reason, ct);
        return handedOff is null ? NotFound() : Ok(new { handedOff });
    }
}
