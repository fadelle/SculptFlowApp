using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Business.Contracts.Engines.Telegram;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Helpers;

namespace PlasticSurgery.Controllers.Integrations;

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
    private readonly ITelegramWebhookProcessor _processor;

    public TelegramWebhookController(ITelegramWebhookProcessor processor)
    {
        _processor = processor;
    }

    [HttpPost("{connectionId:guid}")]
    public async Task<IActionResult> Receive(Guid connectionId, [FromBody] JsonElement update, CancellationToken ct)
    {
        var provided = Request.Headers[TelegramWebhookSecret.HeaderName].FirstOrDefault();
        return await _processor.ReceiveAsync(connectionId, provided, update, ct) switch
        {
            WebhookReceiveOutcome.NotFound => NotFound(),
            WebhookReceiveOutcome.Forbidden => StatusCode(StatusCodes.Status403Forbidden),
            _ => Ok()
        };
    }
}
