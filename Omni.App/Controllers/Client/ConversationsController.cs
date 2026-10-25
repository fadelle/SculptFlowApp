using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Controllers.Filters;
using Omni.App.Entities.Requests.Inbox;
using Omni.App.Entities.Responses.Inbox;

namespace Omni.App.Controllers.Client;

/// <summary>
/// The Inbox's conversation APIs. Every action is individually [Authorize]d and resolves clientId via
/// ICurrentClientContext, same as everywhere else (the AI bot's sends go through AiController).
/// </summary>
[ApiController]
[Route("api/conversations")]
[ApiErrors]
public class ConversationsController : ControllerBase
{
    private readonly IConversationService _conversations;
    private readonly IMessageService _messages;
    private readonly ICurrentClientContext _clientContext;

    public ConversationsController(
        IConversationService conversations, IMessageService messages, ICurrentClientContext clientContext)
    {
        _conversations = conversations;
        _messages = messages;
        _clientContext = clientContext;
    }

    [Authorize]
    [HttpPost]
    public async Task<ActionResult<ConversationResponse>> Create([FromBody] CreateConversationRequest request, CancellationToken ct)
    {
        var clientId = await _clientContext.GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var conversation = await _conversations.CreateAsync(request with { ClientId = clientId.Value }, ct);
        return CreatedAtAction(nameof(GetById), new { id = conversation.Id }, conversation);
    }

    /// <summary>Inbox conversation list (left pane) — newest activity first.</summary>
    [Authorize]
    [HttpGet]
    public async Task<ActionResult<object>> List([FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var clientId = await _clientContext.GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var (items, totalCount) = await _conversations.ListAsync(clientId.Value, Math.Max(skip, 0), Math.Clamp(take, 1, 200), ct);
        return Ok(new { items, totalCount });
    }

    [Authorize]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<object>> GetById(Guid id, CancellationToken ct)
    {
        var clientId = await _clientContext.GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var result = await _conversations.GetByIdWithMessagesAsync(clientId.Value, id, ct);
        if (result is null) return NotFound();

        var (conversation, messages) = result.Value;
        return Ok(new { conversation, messages });
    }

    [Authorize]
    [HttpGet("{id:guid}/messages")]
    public async Task<ActionResult<IReadOnlyList<MessageResponse>>> GetMessages(Guid id, CancellationToken ct)
    {
        var clientId = await _clientContext.GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var messages = await _conversations.GetMessagesAsync(clientId.Value, id, ct);
        return Ok(messages);
    }

    [Authorize]
    [HttpPost("{id:guid}/messages")]
    public async Task<ActionResult<MessageResponse>> AddMessage(Guid id, [FromBody] CreateMessageRequest request, CancellationToken ct)
    {
        var clientId = await _clientContext.GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var message = await _conversations.AddMessageAsync(clientId.Value, id, request, ct);
        return message is null ? NotFound() : Ok(message);
    }

    /// <summary>A member replies from the Inbox (MessageService.SendAsync): flips the conversation to human mode. Fails with
    /// 409 (via [ApiErrors]) if the 24-hour WhatsApp customer service window is closed — the client should offer SendTemplate
    /// instead. The AI bot replies through POST /api/ai/conversations/{id}/reply instead.</summary>
    [Authorize]
    [HttpPost("{id:guid}/messages/send")]
    public async Task<ActionResult<MessageResponse>> SendMessage(Guid id, [FromBody] SendMessageRequest request, CancellationToken ct)
    {
        var clientId = await _clientContext.GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var message = await _messages.SendAsync(clientId.Value, id, request, ct);
        return message is null ? NotFound() : Ok(message);
    }

    /// <summary>A member sends an approved WhatsApp template — usable any time, and the only option
    /// once the service window is closed. See MessageService.SendTemplateAsync.</summary>
    [Authorize]
    [HttpPost("{id:guid}/messages/send-template")]
    public async Task<ActionResult<MessageResponse>> SendTemplateMessage(Guid id, [FromBody] SendTemplateMessageRequest request, CancellationToken ct)
    {
        var clientId = await _clientContext.GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var message = await _messages.SendTemplateAsync(clientId.Value, id, request, ct);
        return message is null ? NotFound() : Ok(message);
    }

    /// <summary>A member opened the conversation — clears its unread marker.</summary>
    [Authorize]
    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        var clientId = await _clientContext.GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        return await _conversations.MarkReadAsync(clientId.Value, id, ct) ? NoContent() : NotFound();
    }

    [Authorize]
    [HttpPost("{id:guid}/take-over")]
    public async Task<ActionResult<ConversationResponse>> TakeOver(Guid id, CancellationToken ct)
    {
        var clientId = await _clientContext.GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var conversation = await _conversations.TakeOverAsync(clientId.Value, id, ct);
        return conversation is null ? NotFound() : Ok(conversation);
    }

    [Authorize]
    [HttpPost("{id:guid}/return-to-ai")]
    public async Task<ActionResult<ConversationResponse>> ReturnToAi(Guid id, CancellationToken ct)
    {
        var clientId = await _clientContext.GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var conversation = await _conversations.ReturnToAiAsync(clientId.Value, id, ct);
        return conversation is null ? NotFound() : Ok(conversation);
    }

    [Authorize]
    [HttpPost("{id:guid}/close")]
    public async Task<ActionResult<ConversationResponse>> Close(Guid id, CancellationToken ct)
    {
        var clientId = await _clientContext.GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var conversation = await _conversations.CloseAsync(clientId.Value, id, ct);
        return conversation is null ? NotFound() : Ok(conversation);
    }

    /// <summary>A member brings a closed conversation back into the working list (a contact's next message does the
    /// same automatically).</summary>
    [Authorize]
    [HttpPost("{id:guid}/reopen")]
    public async Task<ActionResult<ConversationResponse>> Reopen(Guid id, CancellationToken ct)
    {
        var clientId = await _clientContext.GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var conversation = await _conversations.ReopenAsync(clientId.Value, id, ct);
        return conversation is null ? NotFound() : Ok(conversation);
    }
}
