using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Business.Contracts.Engines.Infobip;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Helpers;

namespace PlasticSurgery.Controllers.Integrations;

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
    private readonly IInfobipWhatsAppWebhookProcessor _processor;

    public InfobipWhatsAppWebhookController(IInfobipWhatsAppWebhookProcessor processor)
    {
        _processor = processor;
    }

    [HttpPost("{connectionId:guid}/events")]
    public async Task<IActionResult> Receive(
        Guid connectionId, [FromQuery(Name = InfobipWebhookUrls.TokenQueryName)] string? token,
        [FromBody] JsonElement body, CancellationToken ct)
    {
        return ToActionResult(await _processor.ReceiveAsync(connectionId, token, body, ct));
    }

    /// <summary>Account-level events (WhatsApp template status / category / quality updates), which Infobip sends
    /// for the whole account rather than per sender. Authenticated by Infobip:WebhookToken in ?token=; the endpoint
    /// is off (404) until that's configured. Neutral path on purpose, like the per-connection one.</summary>
    [HttpPost("~/api/integrations/whatsapp/account/events")]
    public async Task<IActionResult> ReceiveAccountEvent(
        [FromQuery(Name = InfobipWebhookUrls.TokenQueryName)] string? token,
        [FromBody] JsonElement body, CancellationToken ct)
    {
        return ToActionResult(await _processor.ReceiveAccountEventAsync(token, body, ct));
    }

    private IActionResult ToActionResult(WebhookReceiveOutcome outcome) => outcome switch
    {
        WebhookReceiveOutcome.NotFound => NotFound(),
        WebhookReceiveOutcome.Forbidden => StatusCode(StatusCodes.Status403Forbidden),
        _ => Ok()
    };
}
