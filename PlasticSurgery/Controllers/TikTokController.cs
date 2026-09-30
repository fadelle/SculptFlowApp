using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace PlasticSurgery.Controllers;

/// <summary>
/// Placeholder for the upcoming TikTok integration — just a reachable webhook endpoint for now, nothing
/// wired to a real feature yet. Deliberately open (no [Authorize], no [RequireIngestKey]): TikTok itself
/// calls this directly and can't present either. GET exists only as a basic reachability check; TikTok's own
/// webhook setup (Developer Portal -> app -> Webhooks) verifies a URL by POSTing a test event and expecting
/// a 200 back, not a challenge/echo handshake like Meta's.
///
/// NOTE — not implemented here, flagged for awareness: once this is real, every POST should verify the
/// payload's `client_key` matches this app's TikTok client key (the actual authenticity check for this
/// endpoint) before acting on it. Every payload is currently only logged, never parsed or acted on.
/// </summary>
[ApiController]
[Route("api/integrations/tiktok/webhook")]
public class TikTokController : ControllerBase
{
    private readonly ILogger<TikTokController> _logger;

    public TikTokController(ILogger<TikTokController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Reachable() => Ok("TikTok webhook endpoint is reachable.");

    /// <summary>Every webhook delivery from TikTok. Just logs the raw body for now — nothing parses or
    /// acts on it until the real integration is built.</summary>
    [HttpPost]
    public IActionResult Receive([FromBody] JsonElement rawBody)
    {
        _logger.LogInformation("TikTok webhook received: {Body}", rawBody.GetRawText());
        return Ok();
    }
}
