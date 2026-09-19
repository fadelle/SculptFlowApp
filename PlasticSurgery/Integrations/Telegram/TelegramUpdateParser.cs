using System.Text.Json;
using System.Text.RegularExpressions;

namespace PlasticSurgery.Integrations.Telegram;

public enum TelegramUpdateKind
{
    /// <summary>A private-chat message from a person to the bot — the only kind handled today.</summary>
    CustomerMessage,
    /// <summary>Anything else (edited messages, group chats, channel posts, bot senders, ...) —
    /// acknowledged with 200 so Telegram doesn't retry, and otherwise dropped.</summary>
    Ignored
}

/// <summary>A Telegram Update reduced to what SculptFlow cares about. Nothing downstream of the parser
/// ever sees Telegram's JSON.</summary>
public record ParsedTelegramUpdate(TelegramUpdateKind Kind, long UpdateId, ParsedTelegramMessage? Message, string? IgnoredReason);

/// <summary>
/// A normalized inbound Telegram message. <c>MessageType</c> uses the same vocabulary the WhatsApp
/// parser produces (text, image, audio, video, document, sticker, location, contacts) plus "command"
/// for bot commands like /start and "unsupported" — only "text" is ever AI-eligible (see
/// MessageService.IngestAsync).
/// </summary>
public record ParsedTelegramMessage(
    long ChatId,
    long? FromUserId,
    string? FirstName,
    string? LastName,
    string? Username,
    long MessageId,
    string MessageType,
    string Content,
    string? MetadataJson,
    DateTimeOffset Timestamp);

/// <summary>
/// The only place that knows Telegram's Update JSON shape (the counterpart of MetaWebhookParser).
///
/// Telegram Business seam: today only the "message" update is parsed. Business-connected accounts
/// deliver "business_message" / "business_connection" updates instead — supporting them later means
/// adding a branch here that maps a business_message onto the same <see cref="ParsedTelegramMessage"/>
/// (plus a business_connection_id -> channel_integration lookup in the webhook controller). Lead,
/// Conversation, Message, Inbox, n8n and the send path would not change.
/// </summary>
public static class TelegramUpdateParser
{
    // "/start", "/start payload" (payload is ignored), "/help@MyBot"
    private static readonly Regex CommandPattern = new(@"^/[A-Za-z0-9_]{1,32}(@[A-Za-z0-9_]{1,64})?(\s|$)", RegexOptions.Compiled);

    public static ParsedTelegramUpdate Parse(JsonElement update)
    {
        var updateId = update.ValueKind == JsonValueKind.Object
                       && update.TryGetProperty("update_id", out var id) && id.ValueKind == JsonValueKind.Number
            ? id.GetInt64() : 0;

        if (update.ValueKind != JsonValueKind.Object)
        {
            return Ignored(updateId, "not_an_object");
        }

        if (!update.TryGetProperty("message", out var m) || m.ValueKind != JsonValueKind.Object)
        {
            return Ignored(updateId, "unsupported_update_type");
        }

        if (!m.TryGetProperty("chat", out var chat) || chat.ValueKind != JsonValueKind.Object
            || !chat.TryGetProperty("id", out var chatIdEl) || chatIdEl.ValueKind != JsonValueKind.Number)
        {
            return Ignored(updateId, "no_chat");
        }

        // Only 1:1 chats with the bot are leads; groups/supergroups/channels are not patient conversations.
        if (GetString(chat, "type") != "private")
        {
            return Ignored(updateId, "non_private_chat");
        }

        JsonElement from = default;
        var hasFrom = m.TryGetProperty("from", out from) && from.ValueKind == JsonValueKind.Object;
        if (hasFrom && from.TryGetProperty("is_bot", out var isBot) && isBot.ValueKind == JsonValueKind.True)
        {
            return Ignored(updateId, "bot_sender");
        }

        if (!m.TryGetProperty("message_id", out var messageIdEl) || messageIdEl.ValueKind != JsonValueKind.Number)
        {
            return Ignored(updateId, "no_message_id");
        }

        var (type, content, metadata) = Classify(m);

        var timestamp = m.TryGetProperty("date", out var dateEl) && dateEl.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds(dateEl.GetInt64())
            : DateTimeOffset.UtcNow;

        var source = hasFrom ? from : chat;
        var parsed = new ParsedTelegramMessage(
            ChatId: chatIdEl.GetInt64(),
            FromUserId: hasFrom && from.TryGetProperty("id", out var fromId) && fromId.ValueKind == JsonValueKind.Number ? fromId.GetInt64() : null,
            FirstName: GetString(source, "first_name"),
            LastName: GetString(source, "last_name"),
            Username: GetString(source, "username"),
            MessageId: messageIdEl.GetInt64(),
            MessageType: type,
            Content: content,
            MetadataJson: metadata,
            Timestamp: timestamp);

        return new ParsedTelegramUpdate(TelegramUpdateKind.CustomerMessage, updateId, parsed, null);
    }

    /// <summary>(MessageType, human-readable Content, raw metadata JSON) for a message body.</summary>
    private static (string Type, string Content, string? Metadata) Classify(JsonElement m)
    {
        var text = GetString(m, "text");
        if (text is not null)
        {
            return CommandPattern.IsMatch(text) ? ("command", text, null) : ("text", text, null);
        }

        var caption = GetString(m, "caption");

        // Media: keep the raw object (it carries file_id, needed if we ever download it) as metadata;
        // Content is the caption if there is one, else a "[photo]"-style placeholder so the Inbox
        // shows something meaningful — same convention as the WhatsApp parser.
        (string Property, string Type, string Placeholder)[] media =
        {
            ("photo", "image", "[photo]"),
            ("voice", "audio", "[voice message]"),
            ("audio", "audio", "[audio]"),
            ("video", "video", "[video]"),
            ("video_note", "video", "[video message]"),
            ("document", "document", "[document]"),
            ("sticker", "sticker", "[sticker]"),
            ("location", "location", "[location]"),
            ("contact", "contacts", "[contact]"),
        };

        foreach (var (property, type, placeholder) in media)
        {
            if (m.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                return (type, string.IsNullOrWhiteSpace(caption) ? placeholder : caption!, value.GetRawText());
            }
        }

        return ("unsupported", "[unsupported message]", m.GetRawText());
    }

    private static ParsedTelegramUpdate Ignored(long updateId, string reason) =>
        new(TelegramUpdateKind.Ignored, updateId, null, reason);

    private static string? GetString(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
