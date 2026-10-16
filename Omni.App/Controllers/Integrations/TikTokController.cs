using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Omni.App.Controllers.Integrations;

/// <summary>
/// Placeholder for the upcoming TikTok integration: a reachable webhook endpoint that checks every delivery's
/// signature and logs a size, nothing wired to a real feature yet. Deliberately open (no [Authorize], no
/// [RequireIngestKey]): TikTok itself calls this directly and can't present either, so the signature is the check.
///
/// TikTok signs each POST with `TikTok-Signature: t=&lt;unix seconds&gt;,s=&lt;hex&gt;`, where s is HMAC-SHA256 of
/// "&lt;t&gt;.&lt;raw body&gt;" keyed with the app's client secret (TikTok:ClientSecret). A missing secret, a bad
/// signature or a timestamp more than five minutes old is answered with 401 and nothing is read further.
/// GET exists only as a basic reachability check.
/// </summary>
[ApiController]
[Route("api/integrations/tiktok/webhook")]
public class TikTokController : ControllerBase
{
    public const string SignatureHeader = "TikTok-Signature";
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    private readonly ILogger<TikTokController> _logger;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _time;

    public TikTokController(ILogger<TikTokController> logger, IConfiguration configuration, TimeProvider? time = null)
    {
        _logger = logger;
        _configuration = configuration;
        _time = time ?? TimeProvider.System;
    }

    [HttpGet]
    public IActionResult Reachable() => Ok("TikTok webhook endpoint is reachable.");

    /// <summary>Every webhook delivery from TikTok. Verifies the signature, then only logs the body size: nothing
    /// parses or acts on the payload until the real integration is built.</summary>
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

        try
        {
            using var _ = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        // Log only a size, never the body.
        _logger.LogInformation("TikTok webhook received ({Length} bytes).", body.Length);
        return Ok();
    }

    private bool HasValidSignature(byte[] body)
    {
        var secret = _configuration["TikTok:ClientSecret"];
        if (string.IsNullOrEmpty(secret))
        {
            _logger.LogError("TikTok:ClientSecret is not configured; rejecting TikTok webhook delivery.");
            return false;
        }

        string? timestamp = null, signature = null;
        foreach (var part in (Request.Headers[SignatureHeader].FirstOrDefault() ?? string.Empty).Split(','))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2) continue;
            if (pair[0].Trim() == "t") timestamp = pair[1].Trim();
            else if (pair[0].Trim() == "s") signature = pair[1].Trim();
        }

        if (!long.TryParse(timestamp, out var seconds) || string.IsNullOrEmpty(signature))
        {
            _logger.LogWarning("TikTok webhook delivery rejected: missing or malformed {Header}.", SignatureHeader);
            return false;
        }

        var age = _time.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(seconds);
        if (age > MaxAge || age < -MaxAge)
        {
            _logger.LogWarning("TikTok webhook delivery rejected: timestamp outside the allowed window.");
            return false;
        }

        var signed = Encoding.UTF8.GetBytes(timestamp + ".").Concat(body).ToArray();
        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed)).ToLowerInvariant();
        var ok = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(signature.ToLowerInvariant()), Encoding.UTF8.GetBytes(expected));
        if (!ok) _logger.LogWarning("TikTok webhook delivery rejected: invalid signature.");
        return ok;
    }
}
