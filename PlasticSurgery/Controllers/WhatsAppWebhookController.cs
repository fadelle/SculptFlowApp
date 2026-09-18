using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Integrations.WhatsApp;
using PlasticSurgery.Services;

namespace PlasticSurgery.Controllers;

/// <summary>
/// Meta calls this directly now — no n8n in between for the inbound side (see the architecture note
/// below). Deliberately open (no [Authorize], no [RequireIngestKey]): Meta itself has no way to
/// present either of those, and the GET handshake is Meta's own trust mechanism for this endpoint.
///
/// GET is Meta's webhook subscription verification handshake (hub.mode/hub.verify_token/hub.challenge).
/// POST is every subsequent webhook delivery — raw Meta JSON in.
///
/// Both actions are thin: all Meta-specific parsing, clinic resolution, persistence, and SignalR
/// broadcasting already lives in Integrations/WhatsApp/MetaWebhookProcessor.cs (and the Handlers
/// beneath it) — this controller does not duplicate any of that, it only decides, from the flat
/// WhatsAppWebhookResponse the processor already returns, whether to make the one new outbound
/// call: notifying n8n's AI workflow when (and only when) ShouldRunAi is true. Non-AI events
/// (statuses, echoes, templates, health, unknown) never reach n8n at all — see IAiTriggerNotifier.
///
/// Architecture, before vs. after:
///   Before:  Meta -> n8n (classifies + forwards raw JSON) -> this app
///   Now:     Meta -> this app (classifies + processes) -> n8n (normalized AI trigger only, and only
///            for eligible customer messages)
/// The final AI-send flow is unchanged: n8n's AI Agent still finishes by calling
/// POST /api/conversations/{conversationId}/messages/send, which still rechecks conversation.mode
/// immediately before actually sending (see ConversationsController.SendMessage).
///
/// NOTE — not implemented here, flagged for awareness: Meta signs real webhook POSTs with an
/// X-Hub-Signature-256 header (HMAC-SHA256 over the raw body, keyed with Meta:AppSecret). Verifying
/// it is the actual authenticity check for this endpoint once Meta calls it directly (the GET
/// handshake only covers the one-time subscription setup, not each delivery) — wasn't requested
/// here, so this endpoint currently accepts any POST body without that check.
/// </summary>
[ApiController]
[Route("api/integrations/whatsapp/webhook")]
public class WhatsAppWebhookController : ControllerBase
{
    private readonly IMetaWebhookProcessor _webhookProcessor;
    private readonly IAiTriggerNotifier _aiTrigger;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WhatsAppWebhookController> _logger;

    public WhatsAppWebhookController(
        IMetaWebhookProcessor webhookProcessor, IAiTriggerNotifier aiTrigger,
        IConfiguration configuration, ILogger<WhatsAppWebhookController> logger)
    {
        _webhookProcessor = webhookProcessor;
        _aiTrigger = aiTrigger;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Meta's one-time webhook subscription handshake. Echoes hub.challenge back as plain
    /// text when hub.mode is "subscribe" and hub.verify_token matches Meta:WebhookVerifyToken
    /// (configured, never hardcoded); otherwise 403.</summary>
    [HttpGet]
    public IActionResult Verify(
        [FromQuery(Name = "hub.mode")] string? hubMode,
        [FromQuery(Name = "hub.verify_token")] string? hubVerifyToken,
        [FromQuery(Name = "hub.challenge")] string? hubChallenge)
    {
        var expectedToken = _configuration["Meta:WebhookVerifyToken"];

        if (string.Equals(hubMode, "subscribe", StringComparison.Ordinal)
            && !string.IsNullOrEmpty(expectedToken)
            && string.Equals(hubVerifyToken, expectedToken, StringComparison.Ordinal)
            && !string.IsNullOrEmpty(hubChallenge))
        {
            return Content(hubChallenge, "text/plain");
        }

        _logger.LogWarning("WhatsApp webhook verification failed (hub.mode={HubMode}).", hubMode);
        return StatusCode(StatusCodes.Status403Forbidden);
    }

    /// <summary>Every WhatsApp webhook delivery from Meta. Reuses the exact same
    /// MetaWebhookParser -> MetaWebhookProcessor -> Handlers pipeline the earlier n8n-relayed
    /// endpoint used — see the class doc comment for what's unchanged vs. new here.</summary>
    [HttpPost]
    public async Task<IActionResult> Receive([FromBody] JsonElement rawBody, CancellationToken ct)
    {
        var result = await _webhookProcessor.ProcessAsync(rawBody, ct);

        if (result.ShouldRunAi && result.ClinicId.HasValue && result.ConversationId.HasValue)
        {
            await _aiTrigger.NotifyAsync(new AiTriggerPayload(
                result.ClinicId.Value, result.ConversationId.Value, result.LeadId, result.MessageId,
                Channel: "whatsapp", MessageType: result.MessageType, MessageText: result.Content,
                SelectedValue: result.SelectedValue), ct);
        }

        // Meta only needs a fast 200 acknowledgement — the response body isn't inspected.
        return Ok();
    }
}
