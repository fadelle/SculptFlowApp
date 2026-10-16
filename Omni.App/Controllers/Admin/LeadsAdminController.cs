using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Services.PlatformAdmin;
using Omni.App.Controllers.Filters;
using Omni.App.Entities.Requests.PlatformAdmin;

namespace Omni.App.Controllers.Admin;

/// <summary>
/// Platform-admin leads API (internal; called by the Omni.Admin portal): leads, conversations, messages and
/// appointments across clients. Writes answer { clientId }. Errors: 400 invalid input, 404 not found.
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
    public async Task<IActionResult> List([FromQuery] Guid? clientId, [FromQuery] string? status, [FromQuery] string? search,
        [FromQuery] int page, CancellationToken ct) =>
        Ok(await _leads.ListLeadsAsync(clientId, status, search, page, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        await _leads.GetLeadAsync(id, ct) is { } lead ? Ok(lead) : NotFound();

    [HttpGet("{id:guid}/events")]
    public async Task<IActionResult> Events(Guid id, CancellationToken ct) => Ok(await _leads.LeadEventsAsync(id, ct));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] LeadUpdate request, CancellationToken ct) =>
        Ok(await _leads.UpdateLeadAsync(id, request, ct));

    [HttpGet("conversations")]
    public async Task<IActionResult> Conversations([FromQuery] Guid? clientId, [FromQuery] Guid? leadId, [FromQuery] string? channel,
        [FromQuery] string? mode, [FromQuery] string? status, [FromQuery] int page, CancellationToken ct) =>
        Ok(await _leads.ListConversationsAsync(clientId, leadId, channel, mode, status, page, ct));

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
    public async Task<IActionResult> Messages([FromQuery] Guid? clientId, [FromQuery] bool failedOnly, [FromQuery] string? sender,
        [FromQuery] string? search, [FromQuery] int page, CancellationToken ct) =>
        Ok(await _leads.ListMessagesAsync(clientId, failedOnly, sender, search, page, ct));

    [HttpGet("appointments")]
    public async Task<IActionResult> Appointments([FromQuery] Guid? clientId, [FromQuery] Guid? leadId, [FromQuery] string? status,
        [FromQuery] bool upcomingOnly, [FromQuery] int page, CancellationToken ct) =>
        Ok(await _leads.ListAppointmentsAsync(clientId, leadId, status, upcomingOnly, page, ct));

    [HttpPut("appointments/{id:guid}/status")]
    public async Task<IActionResult> SetAppointmentStatus(Guid id, [FromBody] StatusBody request, CancellationToken ct) =>
        Ok(await _leads.SetAppointmentStatusAsync(id, request.Status, ct));
}
