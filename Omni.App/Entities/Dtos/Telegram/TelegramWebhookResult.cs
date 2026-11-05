using System.Text.Json;

namespace Omni.App.Entities.Dtos.Telegram;

/// <summary>What the webhook controller needs back to decide whether to call n8n.</summary>
public record TelegramWebhookResult(
    bool Processed,
    string EventType,
    bool ShouldRunAi,
    Guid? ClientId = null,
    Guid? ConversationId = null,
    Guid? ContactId = null,
    Guid? MessageId = null,
    string? MessageType = null,
    string? Content = null,
    string? Mode = null,
    bool Deduplicated = false,
    // When the contact wrote it and Telegram's per-chat message_id: the bot run queue answers a burst in this order (QA t7-06).
    DateTimeOffset? SentAt = null,
    long? ChatMessageId = null);
