using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Bookings;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Business.Contracts.Services.Knowledge;
using Omni.App.Business.Contracts.Services.Contacts;
using Omni.App.Business.Contracts.Services.Catalog;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Statics;
using Omni.App.Controllers.Filters;
using Omni.App.Entities.Dtos.Ai;
using Omni.App.Entities.Dtos.Bookings;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Ai;
using Omni.App.Entities.Requests.Bookings;
using Omni.App.Entities.Requests.Knowledge;
using Omni.App.Entities.Requests.Contacts;
using Omni.App.Entities.Responses.Ai;
using Omni.App.Entities.Responses.Bookings;
using Omni.App.Entities.Responses.Inbox;
using Omni.App.Entities.Responses.Knowledge;
using Omni.App.Entities.Responses.Contacts;
using Omni.App.Entities.Responses.Catalog;

namespace Omni.App.Controllers.Integrations;

/// <summary>
/// The AI agent's (n8n) tool surface — one trusted entry point per tool the AI can call, all
/// delegating to the same services the dashboard/API already use (no duplicated business logic).
/// Protected the same way as /api/messages/ingest (RequireIngestKey): this is server-to-server
/// traffic from n8n, not something the browser should be able to call directly. Every action still
/// takes clientId explicitly and scopes through it — same multi-client rule as the rest of the API.
///
/// Deliberately narrower than the dashboard's own endpoints where it matters: update_contact can't
/// touch identity fields (name/phone/email), and reschedule/cancel/handoff each log a dedicated
/// event with the AI as the source so staff can see what the AI did and why (see EventTypes).
///
/// Knowledge search (search_knowledge) is POST /api/ai/knowledge/search — see
/// IKnowledgeSearchService. It returns relevant chunks of the client's approved Knowledge Base only;
/// the AI agent composes the answer.
///
/// WHICH TOOL FOR WHAT — rule of thumb: the Knowledge Base is for INFORMATION; the structured tools
/// are for LIVE DATA and ACTIONS. The Knowledge Base is never the source of truth for contact data,
/// booking availability, bookings, conversation state or handoff.
///
///   search_knowledge   → client-approved informational content: hours, address, parking,
///                               policies, booking info, pricing, payment/financing, doctors,
///                               service explanations, preparation, recovery, FAQs.
///                               ("Where is the client?", "What happens at a rhinoplasty booking?")
///   get_contact_context          → what is currently known about THIS contact.
///   update_contact               → write real contact/business data (interest, language, qualification...).
///   get_available_slots       → LIVE booking availability.
///   book_slot         → perform an actual booking (also reschedule_ / cancel_booking).
///   handoff_to_human          → switch the conversation to human handling.
///
/// Overlap that is deliberate for now (nothing removed yet):
///   get_client_info  — hours/address/booking rules also belong in the Knowledge Base, so this
///                      may become redundant. Kept working until the Knowledge Base is proven; then
///                      decide whether it is still needed.
///   get_services   — stays. The Knowledge Base EXPLAINS services in prose; get_services returns
///                      the authoritative structured records (service id, name, active flag,
///                      booking duration) that booking and business logic depend on.
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
    private readonly IConversationService _conversations;
    private readonly IKnowledgeSearchService _knowledgeSearch;

    public AiController(
        IClientContext clientContext, ICatalogService services, IContactService contacts,
        IBookingService bookings, IAvailabilityService availability, IConversationService conversations,
        IKnowledgeSearchService knowledgeSearch)
    {
        _clientContext = clientContext;
        _services = services;
        _contacts = contacts;
        _bookings = bookings;
        _availability = availability;
        _conversations = conversations;
        _knowledgeSearch = knowledgeSearch;
    }

    /// <summary>search_knowledge — semantic search over the client's approved Knowledge Base.
    /// For INFORMATION questions only (hours, address, policies, pricing, doctors, service
    /// explanations, recovery, FAQs) — not contact data, availability, bookings or handoff. clientId is the
    /// one n8n already has in context; the AI supplies only the query text. Returns relevant chunks
    /// (possibly none) — never a composed answer.</summary>
    [HttpPost("knowledge/search")]
    public async Task<ActionResult<KnowledgeSearchResponse>> SearchKnowledge([FromBody] KnowledgeSearchRequest request, CancellationToken ct)
    {
        if (request.ClientId == Guid.Empty || string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest(new { error = "clientId and query are required." });
        }

        try
        {
            return Ok(await _knowledgeSearch.SearchAsync(request.ClientId, request.Query, request.Limit, ct));
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

    /// <summary>get_contact_context — what's already known about the person the AI is talking to.</summary>
    [HttpGet("contacts/{contactId:guid}")]
    public async Task<ActionResult<ContactResponse>> GetContactContext(Guid contactId, [FromQuery] Guid clientId, CancellationToken ct)
    {
        var contact = await _contacts.GetByIdAsync(clientId, contactId, ct);
        return contact is null ? NotFound() : Ok(contact);
    }

    /// <summary>update_contact — service interest, language, timeline, notes, follow-up, qualification.
    /// Cannot change identity fields (name/phone/email) — those come from the contact's actual source.</summary>
    [HttpPatch("contacts/{contactId:guid}")]
    public async Task<ActionResult<ContactResponse>> UpdateContact(
        Guid contactId, [FromQuery] Guid clientId, [FromBody] UpdateContactContextRequest request, CancellationToken ct)
    {
        var contact = await _contacts.UpdateContextAsync(clientId, contactId, request, ct);
        return contact is null ? NotFound() : Ok(contact);
    }

    /// <summary>get_available_slots — real open booking slots from the client's structured availability (weekly schedule,
    /// date exceptions, booking rules, service duration, existing bookings), in the client's timezone. `date` is a local
    /// "yyyy-MM-dd" (default today); `days` (1-14, default 1 when a date is given, else 7) searches that many days from it.
    /// Never computed by the AI. A client that hasn't configured availability returns configured=false and no slots.</summary>
    [HttpGet("bookings/available")]
    public async Task<ActionResult<AvailabilityResponse>> GetAvailableSlots(
        [FromQuery] Guid clientId, [FromQuery] Guid? contactId, [FromQuery] Guid? serviceId, [FromQuery] DateOnly? date,
        [FromQuery] int? days, CancellationToken ct = default)
    {
        // With contactId (always passed by the n8n workflow, never chosen by the AI) the reply also carries the contact's own
        // upcoming bookings and the backend's canCreateNewBooking decision — kept separate from the requested date and slots.
        // Read-only: this endpoint never books, reschedules or cancels.
        ContactBookingContext? context = null;
        if (contactId is { } contact)
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
    /// business outcomes; only Success=true with Operation created/rescheduled means the contact's booking changed.
    /// (Replaces book_slot + reschedule_booking, which remain for now but are deprecated.)</summary>
    [HttpPost("bookings/schedule")]
    public async Task<ActionResult<ScheduleBookingResult>> ScheduleBooking(
        [FromQuery] Guid clientId, [FromQuery] Guid contactId, [FromBody] ScheduleBookingRequest request, CancellationToken ct)
    {
        var o = await _bookings.ScheduleAsync(clientId, contactId, request, ct);
        var instruction = AiResultInstructions.ForSchedule(o.Code, o.Existing?.FirstOrDefault()?.Label, o.RequestedLabel);

        return Ok(new ScheduleBookingResult(
            o.Code is "BOOKED" or "RESCHEDULED", o.Operation, o.Code, o.Message, instruction,
            o.Booking, o.PreviousBooking,
            o.Code is "BOOKED" or "RESCHEDULED" ? null : o.Existing,
            o.Code is "BOOKED" or "RESCHEDULED" ? null : o.RequestedLabel));
    }

    /// <summary>book_slot — DEPRECATED: use schedule_booking (the backend decides book vs reschedule). Kept working for now.</summary>
    [HttpPost("bookings/book")]
    public async Task<ActionResult<BookSlotResult>> BookSlot(
        [FromQuery] Guid clientId, [FromBody] BookSlotRequest request, CancellationToken ct)
    {
        const string NotBooked = "Nothing was booked. Do NOT tell the contact a booking is booked or confirmed. ";

        try
        {
            var booking = await _bookings.BookAvailableSlotAsync(new CreateBookingRequest(
                clientId, request.ContactId, request.ServiceId,
                string.IsNullOrWhiteSpace(request.BookingType) ? "standard" : request.BookingType,
                request.ScheduledStart, request.ScheduledEnd, request.LocationType, request.LocationName, request.Notes), ct);
            // Report the booking in the CLIENT's local time (like get_available_slots), never the stored UTC value the AI
            // could read out to the contact as the wrong hour.
            var upcoming = await _bookings.GetUpcomingForContactAsync(clientId, request.ContactId, ct);
            var local = upcoming.Bookings.FirstOrDefault(a => a.Id == booking.Id);
            return Ok(new BookSlotResult(true, "BOOKED", "Booking made.", Booking: local));
        }
        catch (ContactAlreadyBookedException ex)
        {
            return Ok(new BookSlotResult(false, "EXISTING_UPCOMING_BOOKING",
                "Contact already has an upcoming booking.",
                NotBooked + $"Tell the contact they already have a booking on {ex.Existing.Label}, and offer to reschedule or cancel it instead of booking another.",
                ExistingBooking: ex.Existing));
        }
        catch (SlotUnavailableException ex)
        {
            return Ok(new BookSlotResult(false, "SLOT_UNAVAILABLE", ex.Message,
                NotBooked + "Apologise briefly, call get_available_slots again, and offer the contact other times. Do not retry the same time."));
        }
        catch (ArgumentException ex)
        {
            var contactMissing = ex.Message.StartsWith("Contact not found", StringComparison.OrdinalIgnoreCase);
            return Ok(new BookSlotResult(false, contactMissing ? "CONTACT_NOT_FOUND" : "INVALID_REQUEST", ex.Message,
                NotBooked + "Tell the contact you couldn't complete the booking and that a team member will confirm it shortly."));
        }
    }


    /// <summary>get_my_bookings — this contact's upcoming booked/confirmed bookings (soonest first), in client-local
    /// wording. Use it before rescheduling/cancelling (to get the booking id) and to answer "what do I have booked?".
    /// contactId is the current conversation's contact, supplied by the workflow — not chosen by the AI.</summary>
    [HttpGet("bookings/upcoming")]
    public async Task<ActionResult<UpcomingBookingsResponse>> GetMyBookings(
        [FromQuery] Guid clientId, [FromQuery] Guid contactId, CancellationToken ct)
    {
        return Ok(await _bookings.GetUpcomingForContactAsync(clientId, contactId, ct));
    }

    /// <summary>reschedule_booking — DEPRECATED: use schedule_booking. Contact wants to move THEIR upcoming booking. No booking id is needed: the server
    /// uses this contact's single upcoming booking (bookingId is only for the rare contact with several — 409 then lists them).
    /// The new time is checked exactly like a new booking (hours, exceptions, notice, overlaps); 409 slotUnavailable means pick
    /// another time. Always scoped to contactId — the AI can only ever touch the contact it is talking to.</summary>
    [HttpPost("bookings/reschedule")]
    public async Task<ActionResult<BookingResponse>> RescheduleBooking(
        [FromQuery] Guid clientId, [FromQuery] Guid contactId, [FromQuery] Guid? bookingId,
        [FromBody] RescheduleBookingRequest request, CancellationToken ct)
    {
        try
        {
            var booking = await _bookings.RescheduleAsync(clientId, contactId, bookingId, request.ScheduledStart, request.ScheduledEnd, request.Reason, ct);
            return booking is null
                ? NotFound(new { error = "This contact has no upcoming booking to reschedule. Book a new one instead." })
                : Ok(booking);
        }
        catch (MultipleUpcomingBookingsException ex)
        {
            return Conflict(new { error = ex.Message, multipleUpcoming = true, bookings = ex.Bookings });
        }
        catch (SlotUnavailableException ex)
        {
            return Conflict(new { error = ex.Message, slotUnavailable = true });
        }
    }

    /// <summary>cancel_booking — contact asks to cancel THEIR upcoming booking. No booking id is needed (see reschedule).
    /// Always HTTP 200 for business outcomes; only Success=true means something was canceled. The AI must have the contact's explicit
    /// confirmation before calling it.</summary>
    [HttpPost("bookings/cancel")]
    public async Task<ActionResult<CancelBookingResult>> CancelBooking(
        [FromQuery] Guid clientId, [FromQuery] Guid contactId, [FromQuery] Guid? bookingId,
        [FromBody] CancelBookingRequest request, CancellationToken ct)
    {
        var o = await _bookings.CancelBookingAsync(clientId, contactId, bookingId, request.Reason, ct);
        var instruction = AiResultInstructions.ForCancel(o.Code);

        return Ok(new CancelBookingResult(
            o.Code == "CANCELED", o.Operation, o.Code, o.Message, instruction, o.Booking, o.Existing));
    }

    /// <summary>handoff_to_human — medical question, contact requests staff, AI uncertain, upset
    /// customer, etc. Flips the conversation to human mode, same as the dashboard's Take Over, but
    /// attributed to the AI with the reason recorded on the event.</summary>
    [HttpPost("conversations/{id:guid}/handoff")]
    public async Task<ActionResult<ConversationResponse>> HandoffToHuman(
        Guid id, [FromQuery] Guid clientId, [FromBody] HandoffToHumanRequest request, CancellationToken ct)
    {
        var conversation = await _conversations.HandoffToHumanAsync(clientId, id, request.Reason, ct);
        return conversation is null ? NotFound() : Ok(conversation);
    }
}
