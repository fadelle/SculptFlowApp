using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Engines.WhatsApp;
using Omni.App.Business.Contracts.HttpClients.N8n;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Responses.WhatsApp;
using Omni.App.Entities.Dtos.Monitoring;
using Omni.App.Entities.Dtos.Ai;

namespace Omni.App.Controllers.Integrations;

/// <summary>
/// Meta calls this directly now — no n8n in between for the inbound side (see the architecture note
/// below). Deliberately open (no [Authorize], no [RequireIngestKey]): Meta itself has no way to
/// present either of those, and the GET handshake is Meta's own trust mechanism for this endpoint.
///
/// GET is Meta's webhook subscription verification handshake (hub.mode/hub.verify_token/hub.challenge).
/// POST is every subsequent webhook delivery — raw Meta JSON in.
///
/// Both actions are thin: POST only checks the signature and the JSON, stores the delivery
/// (IWhatsAppWebhookInbox) and answers. Everything else — parsing, client resolution, persistence,
/// SignalR broadcasts (MetaWebhookProcessor and its Handlers) and the AI trigger for eligible customer
/// messages (IAiTriggerNotifier) — runs afterwards in WhatsAppDeliveryWorker (QA t10-10).
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
    private readonly IWhatsAppWebhookInbox _inbox;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WhatsAppWebhookController> _logger;
    private readonly IMessageTracer _tracer;

    public WhatsAppWebhookController(
        IWhatsAppWebhookInbox inbox, IConfiguration configuration, ILogger<WhatsAppWebhookController> logger, IMessageTracer tracer)
    {
        _tracer = tracer;
        _inbox = inbox;
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
    /// Meta:AppSecret) or nothing is processed. A signed delivery is stored and answered at once (QA t10-10: processing took
    /// about 2.3 s per message, so a 30-entry delivery waited 69 s and Meta redelivers slow ones); WhatsAppDeliveryWorker
    /// processes it in the background, in order, through MetaWebhookParser -> MetaWebhookProcessor -> Handlers, then triggers
    /// the AI once per conversation (IWhatsAppWebhookInbox).</summary>
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        var body = buffer.ToArray();

        if (!HasValidSignature(body))
        {
            _tracer.Step(TraceSteps.WebhookRejected, TraceOutcomes.Failed, new TraceIds(Channel: Channel), detail: new { reason = "signature" });
            return Unauthorized();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            _tracer.Step(TraceSteps.WebhookRejected, TraceOutcomes.Failed, new TraceIds(Channel: Channel), detail: new { reason = "invalid_json" });
            return BadRequest();
        }

        // Stored = Meta's 200; processing (and any failure in it) happens in the background and never reaches Meta (QA t10-03,
        // t10-10). Only a delivery we couldn't store answers 500, so Meta sends it again.
        try
        {
            await _inbox.EnqueueAsync(body, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "WhatsApp webhook: the delivery couldn't be stored; Meta will send it again.");
            _tracer.Step(TraceSteps.WebhookEventFailed, TraceOutcomes.Failed, new TraceIds(Channel: Channel), detail: new { kind = "store", error = TraceText.Error(ex) });
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
        return Ok();
    }

    /// <summary>Meta signs every delivery: "sha256=" + hex(HMAC-SHA256(raw body, Meta:AppSecret)). Without a configured secret
    /// nothing is accepted, so a missing setting can never silently reopen the endpoint.</summary>
    private const string Channel = "whatsapp";

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