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
/// Knowledge-base search (get_approved_clinic_answer) is intentionally not implemented yet — no
/// FAQ/knowledge table exists in the schema.
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

    public AiController(
        IClinicContext clinicContext, IProcedureService procedures, ILeadService leads,
        IAppointmentService appointments, IConversationService conversations)
    {
        _clinicContext = clinicContext;
        _procedures = procedures;
        _leads = leads;
        _appointments = appointments;
        _conversations = conversations;
    }

    /// <summary>get_clinic_info — hours, location, contact info, consultation rules.</summary>
    [HttpGet("clinic-info")]
    public async Task<ActionResult<ClinicInfoResponse>> GetClinicInfo([FromQuery] Guid clinicId, CancellationToken ct)
    {
        var clinic = await _clinicContext.GetByIdAsync(clinicId, ct);
        if (clinic is null) return NotFound();

        return Ok(new ClinicInfoResponse(
            clinic.Id, clinic.Name, clinic.Phone, clinic.Email, clinic.Website,
            clinic.Address, clinic.OperatingHours, clinic.ConsultationInfo, clinic.Timezone));
    }

    /// <summary>get_procedures — what procedures the clinic offers, with their ids for booking.</summary>
    [HttpGet("procedures")]
    public async Task<ActionResult<IReadOnlyList<ProcedureResponse>>> GetProcedures(
        [FromQuery] Guid clinicId, [FromQuery] bool activeOnly = true, CancellationToken ct = default)
    {
        var procedures = await _procedures.ListAsync(clinicId, activeOnly, ct);
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
        var appointment = await _appointments.CreateAsync(new CreateAppointmentRequest(
            clinicId, request.LeadId, request.ProcedureId,
            string.IsNullOrWhiteSpace(request.AppointmentType) ? "consultation" : request.AppointmentType,
            request.ScheduledStart, request.ScheduledEnd, request.LocationType, request.LocationName, request.Notes), ct);
        return Ok(appointment);
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
