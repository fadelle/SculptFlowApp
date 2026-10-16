using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Services.PlatformAdmin;
using Omni.App.Controllers.Filters;
using Omni.App.Entities.Requests.PlatformAdmin;

namespace Omni.App.Controllers.Admin;

/// <summary>
/// Platform-admin contacts API (internal; called by the Omni.Admin portal): contacts, conversations, messages and
/// bookings across clients. Writes answer { clientId }. Errors: 400 invalid input, 404 not found.
/// </summary>
[ApiController]
[AllowAnonymous]
[RequirePlatformAdminKey]
[Route("api/platform-admin/contacts")]
[ApiErrors]
public class ContactsAdminController : ControllerBase
{
    private readonly IContactAdminService _contacts;

    public ContactsAdminController(IContactAdminService contacts)
    {
        _contacts = contacts;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? clientId, [FromQuery] string? stage, [FromQuery] string? search,
        [FromQuery] int page, CancellationToken ct) =>
        Ok(await _contacts.ListContactsAsync(clientId, stage, search, page, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        await _contacts.GetContactAsync(id, ct) is { } contact ? Ok(contact) : NotFound();

    [HttpGet("{id:guid}/events")]
    public async Task<IActionResult> Events(Guid id, CancellationToken ct) => Ok(await _contacts.ContactEventsAsync(id, ct));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ContactUpdate request, CancellationToken ct) =>
        Ok(await _contacts.UpdateContactAsync(id, request, ct));

    [HttpGet("conversations")]
    public async Task<IActionResult> Conversations([FromQuery] Guid? clientId, [FromQuery] Guid? contactId, [FromQuery] string? channel,
        [FromQuery] string? mode, [FromQuery] string? status, [FromQuery] int page, CancellationToken ct) =>
        Ok(await _contacts.ListConversationsAsync(clientId, contactId, channel, mode, status, page, ct));

    [HttpGet("conversations/{id:guid}")]
    public async Task<IActionResult> Conversation(Guid id, CancellationToken ct) =>
        await _contacts.GetConversationAsync(id, ct) is { } conversation ? Ok(conversation) : NotFound();

    [HttpGet("conversations/{id:guid}/messages")]
    public async Task<IActionResult> ConversationMessages(Guid id, [FromQuery] int take, CancellationToken ct) =>
        Ok(await _contacts.MessagesAsync(id, take <= 0 ? 500 : take, ct));

    [HttpPut("conversations/{id:guid}/mode")]
    public async Task<IActionResult> SetMode(Guid id, [FromBody] ModeBody request, CancellationToken ct) =>
        Ok(await _contacts.SetModeAsync(id, request.Mode, ct));

    [HttpPut("conversations/{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] StatusBody request, CancellationToken ct) =>
        Ok(await _contacts.SetStatusAsync(id, request.Status, ct));

    [HttpGet("messages")]
    public async Task<IActionResult> Messages([FromQuery] Guid? clientId, [FromQuery] bool failedOnly, [FromQuery] string? sender,
        [FromQuery] string? search, [FromQuery] int page, CancellationToken ct) =>
        Ok(await _contacts.ListMessagesAsync(clientId, failedOnly, sender, search, page, ct));

    [HttpGet("bookings")]
    public async Task<IActionResult> Bookings([FromQuery] Guid? clientId, [FromQuery] Guid? contactId, [FromQuery] string? status,
        [FromQuery] bool upcomingOnly, [FromQuery] int page, CancellationToken ct) =>
        Ok(await _contacts.ListBookingsAsync(clientId, contactId, status, upcomingOnly, page, ct));

    [HttpPut("bookings/{id:guid}/status")]
    public async Task<IActionResult> SetBookingStatus(Guid id, [FromBody] StatusBody request, CancellationToken ct) =>
        Ok(await _contacts.SetBookingStatusAsync(id, request.Status, ct));
}
