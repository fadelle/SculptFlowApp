using System.Text.Json;
using System.Text.RegularExpressions;

namespace PlasticSurgery.Entities.Dtos.Telegram;

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
