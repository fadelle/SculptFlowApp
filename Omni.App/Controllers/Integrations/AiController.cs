using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Appointments;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Business.Contracts.Services.Knowledge;
using Omni.App.Business.Contracts.Services.Leads;
using Omni.App.Business.Contracts.Services.Catalog;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Statics;
using Omni.App.Controllers.Filters;
using Omni.App.Entities.Dtos.Ai;
using Omni.App.Entities.Dtos.Appointments;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Ai;
using Omni.App.Entities.Requests.Appointments;
using Omni.App.Entities.Requests.Knowledge;
using Omni.App.Entities.Requests.Leads;
using Omni.App.Entities.Responses.Ai;
using Omni.App.Entities.Responses.Appointments;
using Omni.App.Entities.Responses.Inbox;
using Omni.App.Entities.Responses.Knowledge;
using Omni.App.Entities.Responses.Leads;
using Omni.App.Entities.Responses.Catalog;

namespace Omni.App.Controllers.Integrations;

/// <summary>
/// The AI agent's (n8n) tool surface — one trusted entry point per tool the AI can call, all
/// delegating to the same services the dashboard/API already use (no duplicated business logic).
/// Protected the same way as /api/messages/ingest (RequireIngestKey): this is server-to-server
/// traffic from n8n, not something the browser should be able to call directly. Every action still
/// takes clientId explicitly and scopes through it — same multi-client rule as the rest of the API.
///
/// Deliberately narrower than the dashboard's own endpoints where it matters: update_lead can't
/// touch identity fields (name/phone/email), and reschedule/cancel/handoff each log a dedicated
/// event with the AI as the source so staff can see what the AI did and why (see EventTypes).
///
/// Knowledge search (search_knowledge) is POST /api/ai/knowledge/search — see
/// IKnowledgeSearchService. It returns relevant chunks of the client's approved Knowledge Base only;
/// the AI agent composes the answer.
///
/// WHICH TOOL FOR WHAT — rule of thumb: the Knowledge Base is for INFORMATION; the structured tools
/// are for LIVE DATA and ACTIONS. The Knowledge Base is never the source of truth for lead data,
/// appointment availability, bookings, conversation state or handoff.
///
///   search_knowledge   → client-approved informational content: hours, address, parking,
///                               policies, consultation info, pricing, payment/financing, doctors,
///                               service explanations, preparation, recovery, FAQs.
///                               ("Where is the client?", "What happens at a rhinoplasty consultation?")
///   get_lead_context          → what is currently known about THIS lead.
///   update_lead               → write real lead/business data (interest, language, qualification...).
///   get_available_slots       → LIVE appointment availability.
///   book_consultation         → perform an actual booking (also reschedule_ / cancel_consultation).
///   handoff_to_human          → switch the conversation to human handling.
///
/// Overlap that is deliberate for now (nothing removed yet):
///   get_client_info  — hours/address/consultation rules also belong in the Knowledge Base, so this
///                      may become redundant. Kept working until the Knowledge Base is proven; then
///                      decide whether it is still needed.
///   get_services   — stays. The Knowledge Base EXPLAINS services in prose; get_services returns
///                      the authoritative structured records (service id, name, active flag,
///                      consultation duration) that booking and business logic depend on.
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
    private readonly ILeadService _leads;
    private readonly IAppointmentService _appointments;
    private readonly IAvailabilityService _availability;
    private readonly IConversationService _conversations;
    private readonly IKnowledgeSearchService _knowledgeSearch;

    public AiController(
        IClientContext clientContext, ICatalogService services, ILeadService leads,
        IAppointmentService appointments, IAvailabilityService availability, IConversationService conversations,
        IKnowledgeSearchService knowledgeSearch)
    {
        _clientContext = clientContext;
        _services = services;
        _leads = leads;
        _appointments = appointments;
        _availability = availability;
        _conversations = conversations;
        _knowledgeSearch = knowledgeSearch;
    }

    /// <summary>search_knowledge — semantic search over the client's approved Knowledge Base.
    /// For INFORMATION questions only (hours, address, policies, pricing, doctors, service
    /// explanations, recovery, FAQs) — not lead data, availability, bookings or handoff. clientId is the
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

    /// <summary>get_client_info — hours, location, contact info, consultation rules. Partly overlaps the
    /// Knowledge Base (search_knowledge), which is now the preferred place for this kind of
    /// information; kept working for now and may be retired later.</summary>
    [HttpGet("client-info")]
    public async Task<ActionResult<ClientInfoResponse>> GetClientInfo([FromQuery] Guid clientId, CancellationToken ct)
    {
        var client = await _clientContext.GetByIdAsync(clientId, ct);
        if (client is null) return NotFound();

        return Ok(new ClientInfoResponse(
            client.Id, client.Name, client.Phone, client.Email, client.Website,
            client.Address, client.OperatingHours, client.ConsultationInfo, client.Timezone));
    }

    /// <summary>get_services — the structured, authoritative service records (id, name, active flag,
    /// consultation duration) needed for booking. Use search_knowledge to EXPLAIN a service;
    /// use this when an actual service id/record is needed.</summary>
    [HttpGet("services")]
    public async Task<ActionResult<IReadOnlyList<ServiceResponse>>> GetServices(
        [FromQuery] Guid clientId, CancellationToken ct = default)
    {
        // Always ACTIVE services only — the AI must never see (and so never suggest or book) an
        // inactive one. Historical use of inactive services (existing leads/appointments/bookings,
        // Campaign filtering) goes through the normal backend queries, not this tool. There is
        // deliberately no activeOnly switch here; a stray activeOnly=false query param is ignored.
        var services = await _services.ListAsync(clientId, activeOnly: true, ct);
        return Ok(services);
    }

    /// <summary>get_lead_context — what's already known about the person the AI is talking to.</summary>
    [HttpGet("leads/{leadId:guid}")]
    public async Task<ActionResult<LeadResponse>> GetLeadContext(Guid leadId, [FromQuery] Guid clientId, CancellationToken ct)
    {
        var lead = await _leads.GetByIdAsync(clientId, leadId, ct);
        return lead is null ? NotFound() : Ok(lead);
    }

    /// <summary>update_lead — service interest, language, timeline, notes, follow-up, qualification.
    /// Cannot change identity fields (name/phone/email) — those come from the lead's actual source.</summary>
    [HttpPatch("leads/{leadId:guid}")]
    public async Task<ActionResult<LeadResponse>> UpdateLead(
        Guid leadId, [FromQuery] Guid clientId, [FromBody] UpdateLeadContextRequest request, CancellationToken ct)
    {
        var lead = await _leads.UpdateContextAsync(clientId, leadId, request, ct);
        return lead is null ? NotFound() : Ok(lead);
    }

    /// <summary>get_available_slots — real open consultation slots from the client's structured availability (weekly schedule,
    /// date exceptions, booking rules, service duration, existing appointments), in the client's timezone. `date` is a local
    /// "yyyy-MM-dd" (default today); `days` (1-14, default 1 when a date is given, else 7) searches that many days from it.
    /// Never computed by the AI. A client that hasn't configured availability returns configured=false and no slots.</summary>
    [HttpGet("appointments/available")]
    public async Task<ActionResult<AvailabilityResponse>> GetAvailableSlots(
        [FromQuery] Guid clientId, [FromQuery] Guid? leadId, [FromQuery] Guid? serviceId, [FromQuery] DateOnly? date,
        [FromQuery] int? days, CancellationToken ct = default)
    {
        // With leadId (always passed by the n8n workflow, never chosen by the AI) the reply also carries the patient's own
        // upcoming appointments and the backend's canCreateNewBooking decision — kept separate from the requested date and slots.
        // Read-only: this endpoint never books, reschedules or cancels.
        PatientBookingContext? context = null;
        if (leadId is { } lead)
        {
            context = await _appointments.GetBookingContextAsync(clientId, lead, ct);
            if (context is null) return NotFound(new { error = "Lead not found for this client." });
        }

        var result = await _availability.GetSlotsAsync(clientId, serviceId, date, days ?? (date is null ? 7 : 1), ct);
        if (context is null) return Ok(result);

        return Ok(result with
        {
            RequestedDate = date?.ToString("yyyy-MM-dd") ?? result.Today,
            HasUpcomingAppointment = context.HasUpcomingAppointment,
            CanCreateNewBooking = context.CanCreateNewBooking,
            BookingBlockReason = context.BookingBlockReason,
            ExistingUpcomingAppointments = context.ExistingUpcomingAppointments
        });
    }

    /// <summary>schedule_consultation — the ONE tool the AI uses to book or move a consultation. The BACKEND decides which from the real
    /// appointment state: none upcoming → book; one upcoming AND confirmReplaceExisting=true → reschedule it; upcoming without consent →
    /// nothing changes and CONFIRMATION_REQUIRED comes back. clientId/leadId are workflow values, never AI-chosen. Always HTTP 200 for
    /// business outcomes; only Success=true with Operation created/rescheduled means the patient's appointment changed.
    /// (Replaces book_consultation + reschedule_consultation, which remain for now but are deprecated.)</summary>
    [HttpPost("appointments/schedule")]
    public async Task<ActionResult<ScheduleConsultationResult>> ScheduleConsultation(
        [FromQuery] Guid clientId, [FromQuery] Guid leadId, [FromBody] ScheduleConsultationRequest request, CancellationToken ct)
    {
        var o = await _appointments.ScheduleAsync(clientId, leadId, request, ct);
        var instruction = AiResultInstructions.ForSchedule(o.Code, o.Existing?.FirstOrDefault()?.Label, o.RequestedLabel);

        return Ok(new ScheduleConsultationResult(
            o.Code is "BOOKED" or "RESCHEDULED", o.Operation, o.Code, o.Message, instruction,
            o.Appointment, o.PreviousAppointment,
            o.Code is "BOOKED" or "RESCHEDULED" ? null : o.Existing,
            o.Code is "BOOKED" or "RESCHEDULED" ? null : o.RequestedLabel));
    }

    /// <summary>book_consultation — DEPRECATED: use schedule_consultation (the backend decides book vs reschedule). Kept working for now.</summary>
    [HttpPost("appointments/book")]
    public async Task<ActionResult<BookConsultationResult>> BookConsultation(
        [FromQuery] Guid clientId, [FromBody] BookConsultationRequest request, CancellationToken ct)
    {
        const string NotBooked = "Nothing was booked. Do NOT tell the patient an appointment is booked or confirmed. ";

        try
        {
            var appointment = await _appointments.BookAvailableSlotAsync(new CreateAppointmentRequest(
                clientId, request.LeadId, request.ServiceId,
                string.IsNullOrWhiteSpace(request.AppointmentType) ? "consultation" : request.AppointmentType,
                request.ScheduledStart, request.ScheduledEnd, request.LocationType, request.LocationName, request.Notes), ct);
            // Report the booking in the CLIENT's local time (like get_available_slots), never the stored UTC value the AI
            // could read out to the patient as the wrong hour.
            var upcoming = await _appointments.GetUpcomingForLeadAsync(clientId, request.LeadId, ct);
            var local = upcoming.Appointments.FirstOrDefault(a => a.Id == appointment.Id);
            return Ok(new BookConsultationResult(true, "BOOKED", "Appointment booked.", Appointment: local));
        }
        catch (LeadAlreadyBookedException ex)
        {
            return Ok(new BookConsultationResult(false, "EXISTING_UPCOMING_APPOINTMENT",
                "Patient already has an upcoming appointment.",
                NotBooked + $"Tell the patient they already have an appointment on {ex.Existing.Label}, and offer to reschedule or cancel it instead of booking another.",
                ExistingAppointment: ex.Existing));
        }
        catch (SlotUnavailableException ex)
        {
            return Ok(new BookConsultationResult(false, "SLOT_UNAVAILABLE", ex.Message,
                NotBooked + "Apologise briefly, call get_available_slots again, and offer the patient other times. Do not retry the same time."));
        }
        catch (ArgumentException ex)
        {
            var leadMissing = ex.Message.StartsWith("Lead not found", StringComparison.OrdinalIgnoreCase);
            return Ok(new BookConsultationResult(false, leadMissing ? "LEAD_NOT_FOUND" : "INVALID_REQUEST", ex.Message,
                NotBooked + "Tell the patient you couldn't complete the booking and that a team member will confirm it shortly."));
        }
    }


    /// <summary>get_my_appointments — this patient's upcoming booked/confirmed appointments (soonest first), in client-local
    /// wording. Use it before rescheduling/cancelling (to get the appointment id) and to answer "what do I have booked?".
    /// leadId is the current conversation's lead, supplied by the workflow — not chosen by the AI.</summary>
    [HttpGet("appointments/upcoming")]
    public async Task<ActionResult<UpcomingAppointmentsResponse>> GetMyAppointments(
        [FromQuery] Guid clientId, [FromQuery] Guid leadId, CancellationToken ct)
    {
        return Ok(await _appointments.GetUpcomingForLeadAsync(clientId, leadId, ct));
    }

    /// <summary>reschedule_consultation — DEPRECATED: use schedule_consultation. Patient wants to move THEIR upcoming appointment. No appointment id is needed: the server
    /// uses this lead's single upcoming appointment (appointmentId is only for the rare lead with several — 409 then lists them).
    /// The new time is checked exactly like a new booking (hours, exceptions, notice, overlaps); 409 slotUnavailable means pick
    /// another time. Always scoped to leadId — the AI can only ever touch the patient it is talking to.</summary>
    [HttpPost("appointments/reschedule")]
    public async Task<ActionResult<AppointmentResponse>> RescheduleConsultation(
        [FromQuery] Guid clientId, [FromQuery] Guid leadId, [FromQuery] Guid? appointmentId,
        [FromBody] RescheduleConsultationRequest request, CancellationToken ct)
    {
        try
        {
            var appointment = await _appointments.RescheduleAsync(clientId, leadId, appointmentId, request.ScheduledStart, request.ScheduledEnd, request.Reason, ct);
            return appointment is null
                ? NotFound(new { error = "This patient has no upcoming appointment to reschedule. Book a new one instead." })
                : Ok(appointment);
        }
        catch (MultipleUpcomingAppointmentsException ex)
        {
            return Conflict(new { error = ex.Message, multipleUpcoming = true, appointments = ex.Appointments });
        }
        catch (SlotUnavailableException ex)
        {
            return Conflict(new { error = ex.Message, slotUnavailable = true });
        }
    }

    /// <summary>cancel_consultation — patient asks to cancel THEIR upcoming appointment. No appointment id is needed (see reschedule).
    /// Always HTTP 200 for business outcomes; only Success=true means something was canceled. The AI must have the patient's explicit
    /// confirmation before calling it.</summary>
    [HttpPost("appointments/cancel")]
    public async Task<ActionResult<CancelConsultationResult>> CancelConsultation(
        [FromQuery] Guid clientId, [FromQuery] Guid leadId, [FromQuery] Guid? appointmentId,
        [FromBody] CancelConsultationRequest request, CancellationToken ct)
    {
        var o = await _appointments.CancelConsultationAsync(clientId, leadId, appointmentId, request.Reason, ct);
        var instruction = AiResultInstructions.ForCancel(o.Code);

        return Ok(new CancelConsultationResult(
            o.Code == "CANCELED", o.Operation, o.Code, o.Message, instruction, o.Appointment, o.Existing));
    }

    /// <summary>handoff_to_human — medical question, patient requests staff, AI uncertain, upset
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
