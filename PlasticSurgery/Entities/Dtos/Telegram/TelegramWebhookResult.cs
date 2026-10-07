using System.Text.Json;

namespace PlasticSurgery.Entities.Dtos.Telegram;

/// <summary>What the webhook controller needs back to decide whether to call n8n.</summary>
public record TelegramWebhookResult(
    bool Processed,
    string EventType,
    bool ShouldRunAi,
    Guid? ClinicId = null,
    Guid? ConversationId = null,
    Guid? LeadId = null,
    Guid? MessageId = null,
    string? MessageType = null,
    string? Content = null,
    string? Mode = null,
    bool Deduplicated = false);
