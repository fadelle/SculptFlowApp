using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Omni.App.Common.Helpers;

/// <summary>
/// A short-lived, signed token for one n8n AI run (QA t4-ai-09: the one ingest key was a master key for every client's AI
/// tools). Omni puts it in the AI trigger (AiTriggerPayload.RunToken); n8n sends it back as <see cref="Header"/> on every
/// tool call, and [BotTool] refuses a call whose token wasn't issued for that client and bot or has expired. So the ingest
/// key alone no longer lets anyone act for a client. Signed with N8n:RunTokenSecret (HMAC-SHA256); without that setting no
/// token is issued or required (local development; set it on every hosted environment, deploy runbook).
/// Format: <c>v1.{expiry unix seconds}.{base64url signature of "v1|clientId|botId|expiry"}</c>.
/// </summary>
public static class AiRunToken
{
    public const string Header = "X-Omni-Run-Token";
    public const string SecretKey = "N8n:RunTokenSecret";

    /// <summary>How long a run may keep calling tools. n8n runs take seconds; a slow model and retries stay well inside this.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    /// <summary>The secret, or null when run tokens are off (no or blank N8n:RunTokenSecret).</summary>
    public static string? Secret(IConfiguration configuration) =>
        configuration[SecretKey] is { } s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

    public static string Issue(string secret, Guid clientId, Guid botId, DateTimeOffset now)
    {
        var expiry = (now + Lifetime).ToUnixTimeSeconds();
        return $"v1.{expiry.ToString(CultureInfo.InvariantCulture)}.{Sign(secret, clientId, botId, expiry)}";
    }

    /// <summary>Whether <paramref name="token"/> was issued with this secret for this client and bot and hasn't expired.</summary>
    public static bool IsValid(string secret, string? token, Guid clientId, Guid botId, DateTimeOffset now)
    {
        var parts = token?.Trim().Split('.');
        if (parts is not { Length: 3 } || parts[0] != "v1") return false;
        if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var expiry)) return false;
        if (expiry < now.ToUnixTimeSeconds() || expiry > (now + Lifetime + TimeSpan.FromMinutes(1)).ToUnixTimeSeconds()) return false;
        var expected = Encoding.ASCII.GetBytes(Sign(secret, clientId, botId, expiry));
        var given = Encoding.ASCII.GetBytes(parts[2]);
        return CryptographicOperations.FixedTimeEquals(expected, given);
    }

    private static string Sign(string secret, Guid clientId, Guid botId, long expiry)
    {
        var data = Encoding.UTF8.GetBytes($"v1|{clientId:D}|{botId:D}|{expiry.ToString(CultureInfo.InvariantCulture)}");
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), data);
        return Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
