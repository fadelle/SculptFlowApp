using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Business.Contracts.Services.PlatformAdmin;
using PlasticSurgery.Controllers.Filters;
using PlasticSurgery.Entities.Requests.PlatformAdmin;

namespace PlasticSurgery.Controllers.Admin;

/// <summary>
/// Platform-admin leads API (internal; called by the SculptFlowAdmin portal): leads, conversations, messages and
/// appointments across clinics. Writes answer { clinicId }. Errors: 400 invalid input, 404 not found.
/// </summary>
[ApiController]
[AllowAnonymous]
[RequirePlatformAdminKey]
[Route("api/platform-admin/leads")]
[ApiErrors]
public class LeadsAdminController : ControllerBase
{
    private readonly ILeadAdminService _leads;

    public LeadsAdminController(ILeadAdminService leads)
    {
        _leads = leads;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? clinicId, [FromQuery] string? status, [FromQuery] string? search,
        [FromQuery] int page, CancellationToken ct) =>
        Ok(await _leads.ListLeadsAsync(clinicId, status, search, page, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        await _leads.GetLeadAsync(id, ct) is { } lead ? Ok(lead) : NotFound();

    [HttpGet("{id:guid}/events")]
    public async Task<IActionResult> Events(Guid id, CancellationToken ct) => Ok(await _leads.LeadEventsAsync(id, ct));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] LeadUpdate request, CancellationToken ct) =>
        Ok(await _leads.UpdateLeadAsync(id, request, ct));

    [HttpGet("conversations")]
    public async Task<IActionResult> Conversations([FromQuery] Guid? clinicId, [FromQuery] Guid? leadId, [FromQuery] string? channel,
        [FromQuery] string? mode, [FromQuery] string? status, [FromQuery] int page, CancellationToken ct) =>
        Ok(await _leads.ListConversationsAsync(clinicId, leadId, channel, mode, status, page, ct));

    [HttpGet("conversations/{id:guid}")]
    public async Task<IActionResult> Conversation(Guid id, CancellationToken ct) =>
        await _leads.GetConversationAsync(id, ct) is { } conversation ? Ok(conversation) : NotFound();

    [HttpGet("conversations/{id:guid}/messages")]
    public async Task<IActionResult> ConversationMessages(Guid id, [FromQuery] int take, CancellationToken ct) =>
        Ok(await _leads.MessagesAsync(id, take <= 0 ? 500 : take, ct));

    [HttpPut("conversations/{id:guid}/mode")]
    public async Task<IActionResult> SetMode(Guid id, [FromBody] ModeBody request, CancellationToken ct) =>
        Ok(await _leads.SetModeAsync(id, request.Mode, ct));

    [HttpPut("conversations/{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] StatusBody request, CancellationToken ct) =>
        Ok(await _leads.SetStatusAsync(id, request.Status, ct));

    [HttpGet("messages")]
    public async Task<IActionResult> Messages([FromQuery] Guid? clinicId, [FromQuery] bool failedOnly, [FromQuery] string? sender,
        [FromQuery] string? search, [FromQuery] int page, CancellationToken ct) =>
        Ok(await _leads.ListMessagesAsync(clinicId, failedOnly, sender, search, page, ct));

    [HttpGet("appointments")]
    public async Task<IActionResult> Appointments([FromQuery] Guid? clinicId, [FromQuery] Guid? leadId, [FromQuery] string? status,
        [FromQuery] bool upcomingOnly, [FromQuery] int page, CancellationToken ct) =>
        Ok(await _leads.ListAppointmentsAsync(clinicId, leadId, status, upcomingOnly, page, ct));

    [HttpPut("appointments/{id:guid}/status")]
    public async Task<IActionResult> SetAppointmentStatus(Guid id, [FromBody] StatusBody request, CancellationToken ct) =>
        Ok(await _leads.SetAppointmentStatusAsync(id, request.Status, ct));
}
