using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Controllers;

/// <summary>
/// The AI agent's (n8n) tool surface — one trusted entry point per tool the AI can call, all
/// delegating to the same services the dashboard/API already use (no duplicated business logic).
/// Protected the same way as /api/messages/ingest (RequireIngestKey): this is server-to-server
/// traffic from n8n, not something the browser should be able to call directly. Every action still
/// takes clinicId explicitly and scopes through it — same multi-clinic rule as the rest of the API.
///
/// Deliberately narrower than the dashboard's own endpoints where it matters: update_lead can't
/// touch identity fields (name/phone/email), and reschedule/cancel/handoff each log a dedicated
/// event with the AI as the source so staff can see what the AI did and why (see EventTypes).
///
/// Knowledge search (search_clinic_knowledge) is POST /api/ai/knowledge/search — see
/// IKnowledgeSearchService. It returns relevant chunks of the clinic's approved Knowledge Base only;
/// the AI agent composes the answer.
///
/// WHICH TOOL FOR WHAT — rule of thumb: the Knowledge Base is for INFORMATION; the structured tools
/// are for LIVE DATA and ACTIONS. The Knowledge Base is never the source of truth for lead data,
/// appointment availability, bookings, conversation state or handoff.
///
///   search_clinic_knowledge   → clinic-approved informational content: hours, address, parking,
///                               policies, consultation info, pricing, payment/financing, doctors,
///                               procedure explanations, preparation, recovery, FAQs.
///                               ("Where is the clinic?", "What happens at a rhinoplasty consultation?")
///   get_lead_context          → what is currently known about THIS lead.
///   update_lead               → write real lead/business data (interest, language, qualification...).
///   get_available_slots       → LIVE appointment availability.
///   book_consultation         → perform an actual booking (also reschedule_ / cancel_consultation).
///   handoff_to_human          → switch the conversation to human handling.
///
/// Overlap that is deliberate for now (nothing removed yet):
///   get_clinic_info  — hours/address/consultation rules also belong in the Knowledge Base, so this
///                      may become redundant. Kept working until the Knowledge Base is proven; then
///                      decide whether it is still needed.
///   get_procedures   — stays. The Knowledge Base EXPLAINS procedures in prose; get_procedures returns
///                      the authoritative structured records (procedure id, name, active flag,
///                      consultation duration) that booking and business logic depend on.
/// </summary>
[ApiController]
[Route("api/ai")]
[RequireIngestKey]
public class AiController : ControllerBase
{
    private readonly IClinicContext _clinicContext;
    private readonly IProcedureService _procedures;
    private readonly ILeadService _leads;
    private readonly IAppointmentService _appointments;
    private readonly IConversationService _conversations;
    private readonly IKnowledgeSearchService _knowledgeSearch;

    public AiController(
        IClinicContext clinicContext, IProcedureService procedures, ILeadService leads,
        IAppointmentService appointments, IConversationService conversations,
        IKnowledgeSearchService knowledgeSearch)
    {
        _clinicContext = clinicContext;
        _procedures = procedures;
        _leads = leads;
        _appointments = appointments;
        _conversations = conversations;
        _knowledgeSearch = knowledgeSearch;
    }

    /// <summary>search_clinic_knowledge — semantic search over the clinic's approved Knowledge Base.
    /// For INFORMATION questions only (hours, address, policies, pricing, doctors, procedure
    /// explanations, recovery, FAQs) — not lead data, availability, bookings or handoff. clinicId is the
    /// one n8n already has in context; the AI supplies only the query text. Returns relevant chunks
    /// (possibly none) — never a composed answer.</summary>
    [HttpPost("knowledge/search")]
    public async Task<ActionResult<KnowledgeSearchResponse>> SearchKnowledge([FromBody] KnowledgeSearchRequest request, CancellationToken ct)
    {
        if (request.ClinicId == Guid.Empty || string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest(new { error = "clinicId and query are required." });
        }

        try
        {
            return Ok(await _knowledgeSearch.SearchAsync(request.ClinicId, request.Query, request.Limit, ct));
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }

    /// <summary>get_clinic_info — hours, location, contact info, consultation rules. Partly overlaps the
    /// Knowledge Base (search_clinic_knowledge), which is now the preferred place for this kind of
    /// information; kept working for now and may be retired later.</summary>
    [HttpGet("clinic-info")]
    public async Task<ActionResult<ClinicInfoResponse>> GetClinicInfo([FromQuery] Guid clinicId, CancellationToken ct)
    {
        var clinic = await _clinicContext.GetByIdAsync(clinicId, ct);
        if (clinic is null) return NotFound();

        return Ok(new ClinicInfoResponse(
            clinic.Id, clinic.Name, clinic.Phone, clinic.Email, clinic.Website,
            clinic.Address, clinic.OperatingHours, clinic.ConsultationInfo, clinic.Timezone));
    }

    /// <summary>get_procedures — the structured, authoritative procedure records (id, name, active flag,
    /// consultation duration) needed for booking. Use search_clinic_knowledge to EXPLAIN a procedure;
    /// use this when an actual procedure id/record is needed.</summary>
    [HttpGet("procedures")]
    public async Task<ActionResult<IReadOnlyList<ProcedureResponse>>> GetProcedures(
        [FromQuery] Guid clinicId, CancellationToken ct = default)
    {
        // Always ACTIVE procedures only — the AI must never see (and so never suggest or book) an
        // inactive one. Historical use of inactive procedures (existing leads/appointments/bookings,
        // Campaign filtering) goes through the normal backend queries, not this tool. There is
        // deliberately no activeOnly switch here; a stray activeOnly=false query param is ignored.
        var procedures = await _procedures.ListAsync(clinicId, activeOnly: true, ct);
        return Ok(procedures);
    }

    /// <summary>get_lead_context — what's already known about the person the AI is talking to.</summary>
    [HttpGet("leads/{leadId:guid}")]
    public async Task<ActionResult<LeadResponse>> GetLeadContext(Guid leadId, [FromQuery] Guid clinicId, CancellationToken ct)
    {
        var lead = await _leads.GetByIdAsync(clinicId, leadId, ct);
        return lead is null ? NotFound() : Ok(lead);
    }

    /// <summary>update_lead — procedure interest, language, timeline, notes, follow-up, qualification.
    /// Cannot change identity fields (name/phone/email) — those come from the lead's actual source.</summary>
    [HttpPatch("leads/{leadId:guid}")]
    public async Task<ActionResult<LeadResponse>> UpdateLead(
        Guid leadId, [FromQuery] Guid clinicId, [FromBody] UpdateLeadContextRequest request, CancellationToken ct)
    {
        try
        {
            var lead = await _leads.UpdateContextAsync(clinicId, leadId, request, ct);
            return lead is null ? NotFound() : Ok(lead);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>get_available_slots — open consultation slots in the next `days` days.</summary>
    [HttpGet("appointments/available")]
    public async Task<ActionResult<IReadOnlyList<AvailableSlotResponse>>> GetAvailableSlots(
        [FromQuery] Guid clinicId, [FromQuery] int days = 7, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 30);
        var slots = await _appointments.GetAvailableSlotsAsync(clinicId, days, ct);
        return Ok(slots);
    }

    /// <summary>book_consultation — the patient picked a specific slot.</summary>
    [HttpPost("appointments/book")]
    public async Task<ActionResult<AppointmentResponse>> BookConsultation(
        [FromQuery] Guid clinicId, [FromBody] BookConsultationRequest request, CancellationToken ct)
    {
        try
        {
            var appointment = await _appointments.CreateAsync(new CreateAppointmentRequest(
                clinicId, request.LeadId, request.ProcedureId,
                string.IsNullOrWhiteSpace(request.AppointmentType) ? "consultation" : request.AppointmentType,
                request.ScheduledStart, request.ScheduledEnd, request.LocationType, request.LocationName, request.Notes), ct);
            return Ok(appointment);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>reschedule_consultation — patient wants to change an existing consultation's time.</summary>
    [HttpPost("appointments/{id:guid}/reschedule")]
    public async Task<ActionResult<AppointmentResponse>> RescheduleConsultation(
        Guid id, [FromQuery] Guid clinicId, [FromBody] RescheduleConsultationRequest request, CancellationToken ct)
    {
        var appointment = await _appointments.RescheduleAsync(clinicId, id, request.ScheduledStart, request.ScheduledEnd, request.Reason, ct);
        return appointment is null ? NotFound() : Ok(appointment);
    }

    /// <summary>cancel_consultation — patient asks to cancel.</summary>
    [HttpPost("appointments/{id:guid}/cancel")]
    public async Task<ActionResult<AppointmentResponse>> CancelConsultation(
        Guid id, [FromQuery] Guid clinicId, [FromBody] CancelConsultationRequest request, CancellationToken ct)
    {
        var appointment = await _appointments.CancelAsync(clinicId, id, request.Reason, ct);
        return appointment is null ? NotFound() : Ok(appointment);
    }

    /// <summary>handoff_to_human — medical question, patient requests staff, AI uncertain, upset
    /// customer, etc. Flips the conversation to human mode, same as the dashboard's Take Over, but
    /// attributed to the AI with the reason recorded on the event.</summary>
    [HttpPost("conversations/{id:guid}/handoff")]
    public async Task<ActionResult<ConversationResponse>> HandoffToHuman(
        Guid id, [FromQuery] Guid clinicId, [FromBody] HandoffToHumanRequest request, CancellationToken ct)
    {
        var conversation = await _conversations.HandoffToHumanAsync(clinicId, id, request.Reason, ct);
        return conversation is null ? NotFound() : Ok(conversation);
    }
}
