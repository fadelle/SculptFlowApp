using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;

namespace PlasticSurgery.Common.Helpers;

/// <summary>The setWebhook secret_token: generated here, stored in channel_integrations.webhook_verify_token,
/// echoed back by Telegram in X-Telegram-Bot-Api-Secret-Token, compared in constant time.</summary>
public static class TelegramWebhookSecret
{
    public const string HeaderName = "X-Telegram-Bot-Api-Secret-Token";

    /// <summary>256 random bits as base64url — 43 chars of [A-Za-z0-9_-], within Telegram's 1–256 limit and allowed alphabet.</summary>
    public static string Generate() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static bool Matches(string? expected, string? provided)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(provided)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(provided));
    }
}
