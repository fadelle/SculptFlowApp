using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Controllers;

/// <summary>
/// Doesn't inherit DashboardApiController like the other dashboard controllers — SendMessage
/// specifically has to serve two different callers with two different trust models on one route
/// (see its own doc comment), so a blanket class-level [Authorize] would break the AI/n8n path.
/// Every other action is individually [Authorize]d and resolves clinicId via ICurrentClinicContext,
/// same as everywhere else.
/// </summary>
[ApiController]
[Route("api/conversations")]
public class ConversationsController : ControllerBase
{
    private readonly IConversationService _conversations;
    private readonly IMessageService _messages;
    private readonly IConfiguration _configuration;
    private readonly ICurrentClinicContext _clinicContext;

    public ConversationsController(
        IConversationService conversations, IMessageService messages, IConfiguration configuration, ICurrentClinicContext clinicContext)
    {
        _conversations = conversations;
        _messages = messages;
        _configuration = configuration;
        _clinicContext = clinicContext;
    }

    [Authorize]
    [HttpPost]
    public async Task<ActionResult<ConversationResponse>> Create([FromBody] CreateConversationRequest request, CancellationToken ct)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        try
        {
            var conversation = await _conversations.CreateAsync(request with { ClinicId = clinicId.Value }, ct);
            return CreatedAtAction(nameof(GetById), new { id = conversation.Id }, conversation);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Inbox conversation list (left pane) — newest activity first.</summary>
    [Authorize]
    [HttpGet]
    public async Task<ActionResult<object>> List([FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        var (items, totalCount) = await _conversations.ListAsync(clinicId.Value, skip, Math.Clamp(take, 1, 200), ct);
        return Ok(new { items, totalCount });
    }

    [Authorize]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<object>> GetById(Guid id, CancellationToken ct)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        var result = await _conversations.GetByIdWithMessagesAsync(clinicId.Value, id, ct);
        if (result is null) return NotFound();

        var (conversation, messages) = result.Value;
        return Ok(new { conversation, messages });
    }

    [Authorize]
    [HttpGet("{id:guid}/messages")]
    public async Task<ActionResult<IReadOnlyList<MessageResponse>>> GetMessages(Guid id, CancellationToken ct)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        var messages = await _conversations.GetMessagesAsync(clinicId.Value, id, ct);
        return Ok(messages);
    }

    [Authorize]
    [HttpPost("{id:guid}/messages")]
    public async Task<ActionResult<MessageResponse>> AddMessage(Guid id, [FromBody] CreateMessageRequest request, CancellationToken ct)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        try
        {
            var message = await _conversations.AddMessageAsync(clinicId.Value, id, request, ct);
            return message is null ? NotFound() : Ok(message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Two callers share this one route (see SendMessageRequest's doc comment) — deliberately
    /// [AllowAnonymous] at the attribute level, with each branch enforcing its own trust model:
    ///   - Staff from the Inbox (Sender omitted): requires a logged-in session (checked manually
    ///     below, since the action itself can't be blanket [Authorize]d) and resolves clinicId via
    ///     ICurrentClinicContext. Goes through WhatsAppService and flips the conversation to human
    ///     mode. See MessageService.SendAsync.
    ///   - The AI agent via n8n (Sender = "ai"): requires the trusted X-Ingest-Key header instead,
    ///     takes clinicId explicitly from the query string (trusted via that header, same pattern as
    ///     AiController), then re-checks conversation.mode == ai immediately before sending (a human
    ///     may have taken over while the AI was generating its reply) and never flips mode. See
    ///     MessageService.SendAiReplyAsync. Returns 409 conversation_in_human_mode if the mode check
    ///     fails — this is the mandatory second mode check the AI send flow requires.
    /// Fails with 409 (staff path) if the 24-hour WhatsApp customer service window is closed — the
    /// client should offer SendTemplate instead.</summary>
    [AllowAnonymous]
    [HttpPost("{id:guid}/messages/send")]
    public async Task<ActionResult<MessageResponse>> SendMessage(
        Guid id, [FromQuery] Guid? clinicId, [FromBody] SendMessageRequest request, CancellationToken ct)
    {
        if (string.Equals(request.Sender, "ai", StringComparison.OrdinalIgnoreCase))
        {
            var expectedKey = _configuration["N8n:IngestApiKey"];
            var providedKey = Request.Headers[RequireIngestKeyAttribute.HeaderName].FirstOrDefault();
            if (string.IsNullOrEmpty(expectedKey) || string.IsNullOrEmpty(providedKey) || providedKey != expectedKey)
            {
                return Unauthorized(new { error = $"AI-origin sends require a valid {RequireIngestKeyAttribute.HeaderName} header." });
            }
            if (clinicId is null)
            {
                return BadRequest(new { error = "clinicId is required for an AI-origin send." });
            }

            try
            {
                var aiMessage = await _messages.SendAiReplyAsync(clinicId.Value, id, request.Content, ct);
                return aiMessage is null ? NotFound() : Ok(aiMessage);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (ConversationNotInAiModeException ex)
            {
                return Conflict(new { sent = false, code = "conversation_in_human_mode", conversationId = id, mode = ex.CurrentMode });
            }
            catch (ServiceWindowClosedException ex)
            {
                return Conflict(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return UnprocessableEntity(new { error = ex.Message });
            }
            catch (WhatsAppSendException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
            }
        }

        // Staff path: manual auth check, since this action as a whole is [AllowAnonymous] for the
        // AI branch above.
        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge();
        }

        var resolvedClinicId = await _clinicContext.GetClinicIdAsync(ct);
        if (resolvedClinicId is null) return Forbid();

        try
        {
            var message = await _messages.SendAsync(resolvedClinicId.Value, id, request, ct);
            return message is null ? NotFound() : Ok(message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ServiceWindowClosedException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
        catch (WhatsAppSendException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
        }
    }

    /// <summary>Staff sends an approved WhatsApp template — usable any time, and the only option
    /// once the service window is closed. See MessageService.SendTemplateAsync.</summary>
    [Authorize]
    [HttpPost("{id:guid}/messages/send-template")]
    public async Task<ActionResult<MessageResponse>> SendTemplateMessage(Guid id, [FromBody] SendTemplateMessageRequest request, CancellationToken ct)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        try
        {
            var message = await _messages.SendTemplateAsync(clinicId.Value, id, request, ct);
            return message is null ? NotFound() : Ok(message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
        catch (WhatsAppSendException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
        }
    }

    [Authorize]
    [HttpPost("{id:guid}/take-over")]
    public async Task<ActionResult<ConversationResponse>> TakeOver(Guid id, CancellationToken ct)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        var conversation = await _conversations.TakeOverAsync(clinicId.Value, id, ct);
        return conversation is null ? NotFound() : Ok(conversation);
    }

    [Authorize]
    [HttpPost("{id:guid}/return-to-ai")]
    public async Task<ActionResult<ConversationResponse>> ReturnToAi(Guid id, CancellationToken ct)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        var conversation = await _conversations.ReturnToAiAsync(clinicId.Value, id, ct);
        return conversation is null ? NotFound() : Ok(conversation);
    }

    [Authorize]
    [HttpPost("{id:guid}/close")]
    public async Task<ActionResult<ConversationResponse>> Close(Guid id, CancellationToken ct)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        var conversation = await _conversations.CloseAsync(clinicId.Value, id, ct);
        return conversation is null ? NotFound() : Ok(conversation);
    }
}
