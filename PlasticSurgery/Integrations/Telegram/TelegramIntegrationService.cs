using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;

namespace PlasticSurgery.Integrations.Telegram;

/// <summary>
/// Connect / refresh / disconnect a clinic's Telegram bot. Everything is scoped by the clinicId the
/// caller resolved from CurrentClinicContext — nothing here reads a clinic from user input.
///
/// Connect (no manual webhook/connectionId steps for the clinic):
///   1. getMe validates the token and gives the bot's id + username
///   2. reuse the clinic's channel_integrations row (or mint its id) — that id IS the connectionId
///   3. generate a random webhook secret
///   4. setWebhook({PublicBaseUrl}/api/integrations/telegram/webhook/{connectionId}, secret_token)
///   5. only then persist (token, secret, bot id/username, status=connected, webhook_status=active)
/// Anything that fails before step 5 leaves the previous state untouched, so a bad token can't
/// leave a half-connected row behind.
/// </summary>
public interface ITelegramIntegrationService
{
    /// <summary>Throws ArgumentException (malformed token), TelegramApiException (Telegram rejected the
    /// token / webhook) or InvalidOperationException (no public URL configured, or the bot is already
    /// connected to a different clinic). Messages are safe to show to staff and never contain the token.</summary>
    Task<ChannelIntegrationResponse> ConnectAsync(Guid clinicId, string? botToken, CancellationToken ct = default);

    /// <summary>Asks Telegram (getWebhookInfo) whether the webhook is still registered to us and
    /// records the answer.</summary>
    Task<ChannelIntegrationResponse> RefreshStatusAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>deleteWebhook (best effort), then deactivate and drop the stored token + secret.
    /// Leads, conversations and messages are untouched.</summary>
    Task DisconnectAsync(Guid clinicId, CancellationToken ct = default);
}

public class TelegramIntegrationService : ITelegramIntegrationService
{
    // "<bot id>:<35-char secret>" — validated loosely so a paste with stray whitespace/quotes is caught early.
    private static readonly Regex TokenPattern = new(@"^\d{5,16}:[A-Za-z0-9_-]{30,64}$", RegexOptions.Compiled);

    private readonly ApplicationDbContext _db;
    private readonly ITelegramBotClient _client;
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<TelegramIntegrationService> _logger;

    public TelegramIntegrationService(
        ApplicationDbContext db, ITelegramBotClient client, IConfiguration configuration,
        IHttpContextAccessor http, ILogger<TelegramIntegrationService> logger)
    {
        _db = db;
        _client = client;
        _configuration = configuration;
        _http = http;
        _logger = logger;
    }

    public async Task<ChannelIntegrationResponse> ConnectAsync(Guid clinicId, string? botToken, CancellationToken ct = default)
    {
        var token = (botToken ?? string.Empty).Trim();
        if (!TokenPattern.IsMatch(token))
        {
            throw new ArgumentException("That doesn't look like a Telegram bot token. Copy it exactly from @BotFather — it looks like 123456789:AA… (no spaces or quotes).");
        }

        var publicBaseUrl = ResolvePublicBaseUrl();

        // 1. Prove the token works and learn which bot it is.
        var bot = await _client.GetMeAsync(token, ct);
        var botId = bot.Id.ToString();

        // One bot has exactly one webhook, so it can serve only one clinic at a time.
        var usedElsewhere = await _db.ChannelIntegrations.AnyAsync(c =>
            c.Channel == ChannelType.Telegram && c.TelegramBotId == botId
            && c.Status == ChannelIntegrationStatus.Connected && c.ClinicId != clinicId, ct);
        if (usedElsewhere)
        {
            throw new InvalidOperationException("This bot is already connected to another clinic. Create a separate bot for each clinic with @BotFather.");
        }

        var row = await _db.ChannelIntegrations.FirstOrDefaultAsync(
            c => c.ClinicId == clinicId && c.Channel == ChannelType.Telegram, ct);

        // Replacing a different bot: unhook the old one first so it stops posting to us (best effort).
        if (row is { Status: ChannelIntegrationStatus.Connected } && !string.IsNullOrEmpty(row.AccessToken)
            && row.TelegramBotId != botId)
        {
            await TryDeleteWebhookAsync(row.AccessToken, ct);
        }

        var connectionId = row?.Id ?? Guid.NewGuid();
        var secret = TelegramWebhookSecret.Generate();
        var webhookUrl = $"{publicBaseUrl}/api/integrations/telegram/webhook/{connectionId}";

        // 2. Register the webhook with our secret. If this throws nothing has been written yet.
        await _client.SetWebhookAsync(token, webhookUrl, secret, ct);

        var now = DateTimeOffset.UtcNow;
        if (row is null)
        {
            row = new ChannelIntegration
            {
                Id = connectionId,
                ClinicId = clinicId,
                Channel = ChannelType.Telegram,
                CreatedAt = now
            };
            _db.ChannelIntegrations.Add(row);
        }

        row.Status = ChannelIntegrationStatus.Connected;
        row.AccessToken = token;
        row.WebhookVerifyToken = secret;
        row.TelegramBotId = botId;
        row.TelegramBotUsername = bot.Username;
        row.DisplayName = string.IsNullOrEmpty(bot.Username) ? bot.FirstName : "@" + bot.Username;
        row.WebhookStatus = WebhookStatus.Active;
        row.WebhookRegisteredAt = now;
        row.LastVerifiedAt = now;
        row.LastError = null;
        row.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Telegram bot {BotId} connected for clinic {ClinicId} (connection {ConnectionId}).", botId, clinicId, connectionId);

        // 3. Confirm with Telegram itself (non-fatal: the webhook is registered either way).
        return await RefreshStatusAsync(clinicId, ct);
    }

    public async Task<ChannelIntegrationResponse> RefreshStatusAsync(Guid clinicId, CancellationToken ct = default)
    {
        var row = await _db.ChannelIntegrations.FirstOrDefaultAsync(
            c => c.ClinicId == clinicId && c.Channel == ChannelType.Telegram, ct);
        if (row is null)
        {
            return new ChannelIntegrationResponse(
                Guid.Empty, clinicId, ChannelType.Telegram, ChannelIntegrationStatus.Disconnected,
                null, null, null, null, null, false, false, null, null, DateTimeOffset.MinValue);
        }

        if (row.Status == ChannelIntegrationStatus.Connected && !string.IsNullOrEmpty(row.AccessToken))
        {
            try
            {
                var info = await _client.GetWebhookInfoAsync(row.AccessToken, ct);
                var expectedSuffix = $"/api/integrations/telegram/webhook/{row.Id}";
                var registeredToUs = info.Url is not null && info.Url.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase);

                row.WebhookStatus = registeredToUs ? WebhookStatus.Active : WebhookStatus.Error;
                row.LastError = !registeredToUs
                    ? "Telegram's webhook for this bot doesn't point at SculptFlow — click Reconnect."
                    : info.LastErrorMessage is not null && info.LastErrorDate > DateTimeOffset.UtcNow.AddHours(-1)
                        ? $"Telegram reported a delivery error: {info.LastErrorMessage}"
                        : null;
                row.LastVerifiedAt = DateTimeOffset.UtcNow;
            }
            catch (TelegramApiException ex)
            {
                row.WebhookStatus = WebhookStatus.Error;
                row.LastError = ex.Message;
            }
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return ToResponse(row);
    }

    public async Task DisconnectAsync(Guid clinicId, CancellationToken ct = default)
    {
        var row = await _db.ChannelIntegrations.FirstOrDefaultAsync(
            c => c.ClinicId == clinicId && c.Channel == ChannelType.Telegram, ct);
        if (row is null) return;

        string? note = null;
        if (row.Status == ChannelIntegrationStatus.Connected && !string.IsNullOrEmpty(row.AccessToken))
        {
            if (!await TryDeleteWebhookAsync(row.AccessToken, ct))
            {
                // Still disconnect locally — inbound is rejected (404) once status != connected, so a
                // leftover webhook at Telegram is inert — but tell staff it wasn't cleaned up.
                note = "Disconnected, but Telegram couldn't be told to remove the webhook. It is harmless and will be replaced on reconnect.";
            }
        }

        // Token and secret are no longer needed; the bot id/username stay for display ("last connected
        // bot") and the row (and its id) stays so history and a later reconnect keep the same connectionId.
        row.Status = ChannelIntegrationStatus.Disconnected;
        row.AccessToken = null;
        row.WebhookVerifyToken = null;
        row.WebhookStatus = WebhookStatus.NotRegistered;
        row.WebhookRegisteredAt = null;
        row.LastVerifiedAt = null;
        row.LastError = note;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Telegram disconnected for clinic {ClinicId} (connection {ConnectionId}).", clinicId, row.Id);
    }

    private async Task<bool> TryDeleteWebhookAsync(string token, CancellationToken ct)
    {
        try
        {
            await _client.DeleteWebhookAsync(token, ct);
            return true;
        }
        catch (TelegramApiException ex)
        {
            _logger.LogWarning("Telegram deleteWebhook failed: {Message}", ex.Message);
            return false;
        }
    }

    /// <summary>App:PublicBaseUrl if set (required for local dev, where the request host is
    /// localhost and unreachable from Telegram); otherwise the current request's own host, but only
    /// when it's a real public host. Telegram requires HTTPS.</summary>
    private string ResolvePublicBaseUrl()
    {
        var configured = _configuration["App:PublicBaseUrl"]?.Trim().TrimEnd('/');
        if (!string.IsNullOrEmpty(configured))
        {
            if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException("App:PublicBaseUrl must be a full https:// URL (Telegram only delivers webhooks over HTTPS).");
            }
            return configured;
        }

        var request = _http.HttpContext?.Request;
        if (request is not null)
        {
            var host = request.Host.Host;
            var isLocal = host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                          || host.StartsWith("127.") || host == "::1" || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
            if (!isLocal)
            {
                return $"https://{request.Host}";
            }
        }

        throw new InvalidOperationException(
            "Telegram needs a public HTTPS address to send messages to. Set App:PublicBaseUrl (e.g. https://your-app.onrender.com, or an https tunnel URL when testing locally).");
    }

    /// <summary>Never includes the token, webhook secret or any other credential.</summary>
    internal static ChannelIntegrationResponse ToResponse(ChannelIntegration c) => new(
        c.Id, c.ClinicId, c.Channel, c.Status, c.DisplayName,
        c.PhoneNumberId, c.WhatsAppBusinessId, c.PageId, c.InstagramBusinessId,
        HasAccessToken: !string.IsNullOrEmpty(c.AccessToken),
        HasWebhookVerifyToken: !string.IsNullOrEmpty(c.WebhookVerifyToken),
        c.LastVerifiedAt, c.LastError, c.UpdatedAt, c.Pin,
        c.TelegramBotId, c.TelegramBotUsername, c.WebhookStatus, c.WebhookRegisteredAt, c.LastWebhookAt);
}

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
