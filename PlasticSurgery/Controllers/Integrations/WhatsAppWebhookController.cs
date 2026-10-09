using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Business.Contracts.Engines.WhatsApp;
using PlasticSurgery.Business.Contracts.HttpClients.N8n;
using PlasticSurgery.Entities.Dtos.Ai;

namespace PlasticSurgery.Controllers.Integrations;

/// <summary>
/// Meta calls this directly now — no n8n in between for the inbound side (see the architecture note
/// below). Deliberately open (no [Authorize], no [RequireIngestKey]): Meta itself has no way to
/// present either of those, and the GET handshake is Meta's own trust mechanism for this endpoint.
///
/// GET is Meta's webhook subscription verification handshake (hub.mode/hub.verify_token/hub.challenge).
/// POST is every subsequent webhook delivery — raw Meta JSON in.
///
/// Both actions are thin: all Meta-specific parsing, clinic resolution, persistence, and SignalR
/// broadcasting already lives in Business/Engines/WhatsApp/MetaWebhookProcessor.cs (and the Handlers
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
/// Authenticity: every POST must carry Meta's X-Hub-Signature-256 (HMAC-SHA256 over the raw body, keyed with Meta:AppSecret).
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

    /// <summary>Every WhatsApp webhook delivery from Meta. The raw body must carry a valid X-Hub-Signature-256 (HMAC-SHA256 keyed with
    /// Meta:AppSecret) or nothing is processed. Then it reuses the MetaWebhookParser -> MetaWebhookProcessor -> Handlers pipeline.
    /// One batch can hold messages from several patients, so the AI is triggered once per conversation (the last message wins).</summary>
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        var body = buffer.ToArray();

        if (!HasValidSignature(body))
        {
            return Unauthorized();
        }

        JsonElement rawBody;
        try
        {
            using var document = JsonDocument.Parse(body);
            rawBody = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        var results = await _webhookProcessor.ProcessAllAsync(rawBody, ct);
        var aiTriggers = results
            .Where(r => r.ShouldRunAi && r.ClinicId.HasValue && r.ConversationId.HasValue)
            .GroupBy(r => r.ConversationId!.Value)
            .Select(g => g.Last());
        foreach (var result in aiTriggers)
        {
            await _aiTrigger.NotifyAsync(new AiTriggerPayload(
                result.ClinicId!.Value, result.ConversationId!.Value, result.LeadId, result.MessageId,
                Channel: "whatsapp", MessageType: result.MessageType, MessageText: result.Content,
                SelectedValue: result.SelectedValue), ct);
        }

        // Meta only needs a fast 200 acknowledgement — the response body isn't inspected.
        return Ok();
    }

    /// <summary>Meta signs every delivery: "sha256=" + hex(HMAC-SHA256(raw body, Meta:AppSecret)). Without a configured secret
    /// nothing is accepted, so a missing setting can never silently reopen the endpoint.</summary>
    private bool HasValidSignature(byte[] body)
    {
        var secret = _configuration["Meta:AppSecret"];
        if (string.IsNullOrEmpty(secret))
        {
            _logger.LogError("Meta:AppSecret is not configured; rejecting WhatsApp webhook delivery.");
            return false;
        }

        var provided = Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
        var expected = "sha256=" + Convert.ToHexString(
            System.Security.Cryptography.HMACSHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret), body)).ToLowerInvariant();
        if (string.IsNullOrEmpty(provided))
        {
            _logger.LogWarning("WhatsApp webhook delivery rejected: missing X-Hub-Signature-256.");
            return false;
        }

        var ok = System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(provided.ToLowerInvariant()), System.Text.Encoding.UTF8.GetBytes(expected));
        if (!ok) _logger.LogWarning("WhatsApp webhook delivery rejected: invalid X-Hub-Signature-256.");
        return ok;
    }
}