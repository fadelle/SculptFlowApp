using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Integrations.Telegram;
using PlasticSurgery.Services;

namespace PlasticSurgery.Controllers;

/// <summary>
/// Telegram posts every bot update here — the Telegram counterpart of WhatsAppWebhookController, and
/// just as thin. Deliberately anonymous (Telegram can't log in); trust comes from two things Telegram
/// echoes back from setWebhook: the connectionId in the URL (= channel_integrations.id) and the secret
/// token header.
///
///   1. connectionId -> channel_integrations row; must be channel=telegram AND status=connected
///      (a disconnected/unknown connection is a plain 404)
///   2. X-Telegram-Bot-Api-Secret-Token must equal that row's stored secret (constant-time) else 403
///   3. clinic_id comes from the STORED row — never from the payload
///   4. TelegramWebhookProcessor does all parsing/persistence/SignalR (via the existing services)
///   5. only if the processor says the message is AI-eligible (customer text, mode = ai, not a
///      duplicate) is n8n notified — the same IAiTriggerNotifier WhatsApp uses
/// Errors after auth surface as 500 on purpose: Telegram retries non-2xx deliveries, and a retry is
/// safe because ingestion is idempotent on (clinic, channel, "chatId:messageId").
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/integrations/telegram/webhook")]
public class TelegramWebhookController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ITelegramWebhookProcessor _processor;
    private readonly IAiTriggerNotifier _aiTrigger;
    private readonly ILogger<TelegramWebhookController> _logger;

    public TelegramWebhookController(
        ApplicationDbContext db, ITelegramWebhookProcessor processor, IAiTriggerNotifier aiTrigger,
        ILogger<TelegramWebhookController> logger)
    {
        _db = db;
        _processor = processor;
        _aiTrigger = aiTrigger;
        _logger = logger;
    }

    [HttpPost("{connectionId:guid}")]
    public async Task<IActionResult> Receive(Guid connectionId, [FromBody] JsonElement update, CancellationToken ct)
    {
        var connection = await _db.ChannelIntegrations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == connectionId, ct);

        if (connection is null
            || connection.Channel != ChannelType.Telegram
            || connection.Status != ChannelIntegrationStatus.Connected)
        {
            return NotFound();
        }

        var provided = Request.Headers[TelegramWebhookSecret.HeaderName].FirstOrDefault();
        if (!TelegramWebhookSecret.Matches(connection.WebhookVerifyToken, provided))
        {
            _logger.LogWarning("Telegram webhook secret mismatch for connection {ConnectionId}.", connectionId);
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var result = await _processor.ProcessAsync(connection, update, ct);

        if (result.ShouldRunAi && result.ClinicId.HasValue && result.ConversationId.HasValue)
        {
            // Same normalized trigger WhatsApp sends — only Channel differs. Never raw Telegram JSON.
            await _aiTrigger.NotifyAsync(new AiTriggerPayload(
                result.ClinicId.Value, result.ConversationId.Value, result.LeadId, result.MessageId,
                Channel: ConversationChannel.Telegram, MessageType: result.MessageType, MessageText: result.Content,
                SelectedValue: null), ct);
        }

        return Ok();
    }
}
