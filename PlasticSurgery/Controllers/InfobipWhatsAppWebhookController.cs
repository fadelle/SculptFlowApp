using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Integrations.Infobip;
using PlasticSurgery.Integrations.Telegram;
using PlasticSurgery.Services;

namespace PlasticSurgery.Controllers;

/// <summary>
/// Infobip posts WhatsApp traffic for one clinic's sender here — inbound messages (configured per sender in
/// the Infobip portal) and delivery + seen reports (requested per message through notifyUrl). The Infobip
/// counterpart of WhatsAppWebhookController, shaped like TelegramWebhookController:
///
///   1. connectionId -> channel_integrations row; must be channel=whatsapp, provider=infobip, status=connected
///      (anything else is a plain 404)
///   2. ?token= must equal that row's stored secret (constant-time) else 403. Infobip's forwarding can't add a
///      custom header, so the per-connection secret rides in the URL, like a signed webhook link.
///   3. clinic_id comes from the STORED row — never from the payload
///   4. InfobipWhatsAppWebhookProcessor does the parsing/persistence via the shared WhatsApp handlers
///   5. n8n gets the same normalized AiTriggerPayload Meta and Telegram send, once per AI-eligible conversation
/// Errors after auth surface as 500 on purpose: Infobip retries non-2xx, and ingestion is idempotent.
/// The token is never logged (Microsoft.AspNetCore request logging is at Warning, and nothing here logs URLs).
/// </summary>
[ApiController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)] // provider-only endpoint; keep it out of the API docs
[Route(InfobipWebhookUrls.RoutePrefix)]
public class InfobipWhatsAppWebhookController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IInfobipWhatsAppWebhookProcessor _processor;
    private readonly IAiTriggerNotifier _aiTrigger;
    private readonly ILogger<InfobipWhatsAppWebhookController> _logger;

    public InfobipWhatsAppWebhookController(
        ApplicationDbContext db, IInfobipWhatsAppWebhookProcessor processor, IAiTriggerNotifier aiTrigger,
        ILogger<InfobipWhatsAppWebhookController> logger)
    {
        _db = db;
        _processor = processor;
        _aiTrigger = aiTrigger;
        _logger = logger;
    }

    [HttpPost("{connectionId:guid}/events")]
    public async Task<IActionResult> Receive(
        Guid connectionId, [FromQuery(Name = InfobipWebhookUrls.TokenQueryName)] string? token,
        [FromBody] JsonElement body, CancellationToken ct)
    {
        var connection = await _db.ChannelIntegrations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == connectionId, ct);

        if (connection is null
            || connection.Channel != ChannelType.WhatsApp
            || ChannelProvider.Of(connection) != ChannelProvider.Infobip
            || connection.Status != ChannelIntegrationStatus.Connected)
        {
            return NotFound();
        }

        // Same constant-time compare the Telegram webhook uses for its per-connection secret.
        if (!TelegramWebhookSecret.Matches(connection.WebhookVerifyToken, token))
        {
            _logger.LogWarning("Infobip webhook token mismatch for connection {ConnectionId}.", connectionId);
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        // A template update posted to a clinic's URL (if the account-level subscription points here) is handled
        // like on the account endpoint below — it's matched to its clinic by template id, never by this URL.
        if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("messageTemplateId", out _))
        {
            await _processor.ProcessTemplateUpdateAsync(body, ct);
            return Ok();
        }

        var results = await _processor.ProcessAsync(connection, body, ct);

        // One AI turn per conversation per delivery: if a batch holds several messages from the same patient,
        // the AI answers once, after the last of them (it reads the whole conversation anyway).
        var aiTriggers = results
            .Where(r => r.ShouldRunAi && r.ClinicId.HasValue && r.ConversationId.HasValue)
            .GroupBy(r => r.ConversationId!.Value)
            .Select(g => g.Last());

        foreach (var r in aiTriggers)
        {
            await _aiTrigger.NotifyAsync(new AiTriggerPayload(
                r.ClinicId!.Value, r.ConversationId!.Value, r.LeadId, r.MessageId,
                Channel: ConversationChannel.WhatsApp, MessageType: r.MessageType, MessageText: r.Content,
                SelectedValue: r.SelectedValue), ct);
        }

        return Ok();
    }

    /// <summary>Account-level events (WhatsApp template status / category / quality updates), which Infobip sends
    /// for the whole account rather than per sender. Authenticated by Infobip:WebhookToken in ?token=; the endpoint
    /// is off (404) until that's configured. Neutral path on purpose, like the per-connection one.</summary>
    [HttpPost("~/api/integrations/whatsapp/account/events")]
    public async Task<IActionResult> ReceiveAccountEvent(
        [FromQuery(Name = InfobipWebhookUrls.TokenQueryName)] string? token,
        [FromBody] JsonElement body, [FromServices] IConfiguration configuration, CancellationToken ct)
    {
        var expected = configuration["Infobip:WebhookToken"];
        if (string.IsNullOrEmpty(expected))
        {
            return NotFound();
        }
        if (!TelegramWebhookSecret.Matches(expected, token))
        {
            _logger.LogWarning("Infobip account webhook token mismatch.");
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        await _processor.ProcessTemplateUpdateAsync(body, ct);
        return Ok();
    }
}
