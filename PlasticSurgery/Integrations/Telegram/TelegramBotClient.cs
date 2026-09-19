using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PlasticSurgery.Services;

namespace PlasticSurgery.Integrations.Telegram;

/// <summary>Identity of a bot as reported by Telegram's getMe.</summary>
public record TelegramBotInfo(long Id, string? Username, string? FirstName);

/// <summary>What Telegram itself reports about a bot's webhook (getWebhookInfo).</summary>
public record TelegramWebhookInfo(string? Url, int PendingUpdateCount, string? LastErrorMessage, DateTimeOffset? LastErrorDate);

/// <summary>
/// The one class that talks to the Telegram Bot API — the Telegram counterpart of MetaGraphClient /
/// WhatsAppService. Every method takes the bot token as a parameter; nothing here stores it.
///
/// SECRET HANDLING: Telegram puts the token in the URL path (/bot{token}/method). Two consequences,
/// both handled: (1) this client is registered with RemoveAllLoggers() in Program.cs so the framework's
/// HttpClient logging (which prints request URIs) never sees it; (2) failures are re-thrown as
/// <see cref="TelegramApiException"/> with a message built only from Telegram's error description —
/// never from the request URL.
/// </summary>
public interface ITelegramBotClient
{
    Task<TelegramBotInfo> GetMeAsync(string botToken, CancellationToken ct = default);

    /// <summary>Registers <paramref name="url"/> as the bot's webhook; Telegram will send
    /// <paramref name="secretToken"/> back in X-Telegram-Bot-Api-Secret-Token on every delivery.</summary>
    Task SetWebhookAsync(string botToken, string url, string secretToken, CancellationToken ct = default);

    Task DeleteWebhookAsync(string botToken, CancellationToken ct = default);

    Task<TelegramWebhookInfo> GetWebhookInfoAsync(string botToken, CancellationToken ct = default);

    /// <summary>Sends plain text (no parse_mode, so no markup escaping surprises) and returns the
    /// Telegram message_id of the sent message.</summary>
    Task<long> SendMessageAsync(string botToken, string chatId, string text, CancellationToken ct = default);
}

public class TelegramBotClient : ITelegramBotClient
{
    /// <summary>Telegram's hard limit for a text message.</summary>
    public const int MaxMessageLength = 4096;

    private static readonly JsonSerializerOptions JsonOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly HttpClient _http;
    private readonly string _apiBaseUrl;

    public TelegramBotClient(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        // Overridable only so tests can point at a fake Bot API; production always uses Telegram's.
        _apiBaseUrl = (configuration["Telegram:ApiBaseUrl"] ?? "https://api.telegram.org").TrimEnd('/');
    }

    public async Task<TelegramBotInfo> GetMeAsync(string botToken, CancellationToken ct = default)
    {
        var result = await CallAsync(botToken, "getMe", new { }, ct);
        return new TelegramBotInfo(
            result.GetProperty("id").GetInt64(),
            result.TryGetProperty("username", out var u) ? u.GetString() : null,
            result.TryGetProperty("first_name", out var f) ? f.GetString() : null);
    }

    public async Task SetWebhookAsync(string botToken, string url, string secretToken, CancellationToken ct = default)
    {
        // allowed_updates is explicit so we only ever receive what the parser understands. Adding
        // Telegram Business later means adding "business_connection"/"business_message" here and
        // re-running setWebhook (a reconnect does that) — nothing else about the transport changes.
        await CallAsync(botToken, "setWebhook", new
        {
            url,
            secret_token = secretToken,
            allowed_updates = new[] { "message" }
        }, ct);
    }

    public async Task DeleteWebhookAsync(string botToken, CancellationToken ct = default) =>
        await CallAsync(botToken, "deleteWebhook", new { drop_pending_updates = false }, ct);

    public async Task<TelegramWebhookInfo> GetWebhookInfoAsync(string botToken, CancellationToken ct = default)
    {
        var r = await CallAsync(botToken, "getWebhookInfo", new { }, ct);
        DateTimeOffset? errorDate = r.TryGetProperty("last_error_date", out var d) && d.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds(d.GetInt64()) : null;
        return new TelegramWebhookInfo(
            r.TryGetProperty("url", out var url) ? url.GetString() : null,
            r.TryGetProperty("pending_update_count", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0,
            r.TryGetProperty("last_error_message", out var m) ? m.GetString() : null,
            errorDate);
    }

    public async Task<long> SendMessageAsync(string botToken, string chatId, string text, CancellationToken ct = default)
    {
        if (text.Length > MaxMessageLength)
        {
            throw new TelegramApiException($"Telegram messages are limited to {MaxMessageLength} characters (this one is {text.Length}).");
        }

        // chat_id is a number for private chats; send it as a number when it parses so Telegram
        // never has to guess whether a string is a username.
        object chat = long.TryParse(chatId, out var numericChatId) ? numericChatId : chatId;
        var result = await CallAsync(botToken, "sendMessage", new { chat_id = chat, text }, ct);
        return result.GetProperty("message_id").GetInt64();
    }

    private async Task<JsonElement> CallAsync(string botToken, string method, object payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(botToken))
        {
            throw new TelegramApiException("No Telegram bot token is configured.", errorCode: 401);
        }

        string body;
        int statusCode;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_apiBaseUrl}/bot{botToken}/{method}")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, new MediaTypeHeaderValue("application/json"))
            };
            using var response = await _http.SendAsync(request, ct);
            statusCode = (int)response.StatusCode;
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException) && !ct.IsCancellationRequested)
        {
            // Deliberately not ex.Message/ex.ToString(): keep anything URL-shaped (and so the token) out.
            throw new TelegramApiException($"Could not reach Telegram ({ex.GetType().Name}).");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw new TelegramApiException($"Telegram returned an unreadable response (HTTP {statusCode}).", statusCode);
        }

        using (doc)
        {
            var root = doc.RootElement;
            var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
            if (!ok)
            {
                var description = root.TryGetProperty("description", out var d) ? d.GetString() : null;
                var code = root.TryGetProperty("error_code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : statusCode;
                throw new TelegramApiException(FriendlyMessage(code, description), code);
            }

            return root.TryGetProperty("result", out var result) ? result.Clone() : default;
        }
    }

    private static string FriendlyMessage(int code, string? description) => code switch
    {
        401 => "Telegram rejected the bot token (Unauthorized). Check that it was copied exactly from BotFather.",
        403 => "Telegram refused the message — the user has blocked the bot or never started a chat with it.",
        429 => "Telegram is rate-limiting this bot — try again in a moment.",
        _ => $"Telegram error {code}: {description ?? "unknown error"}"
    };
}

/// <summary>A failed Telegram Bot API call. The message never contains the bot token.</summary>
public class TelegramApiException : ChannelSendException
{
    public int ErrorCode { get; }

    public TelegramApiException(string message, int errorCode = 0) : base(message)
    {
        ErrorCode = errorCode;
    }
}
