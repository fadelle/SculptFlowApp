using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlasticSurgery.Entities.Dtos.Telegram;

/// <summary>What Telegram itself reports about a bot's webhook (getWebhookInfo).</summary>
public record TelegramWebhookInfo(string? Url, int PendingUpdateCount, string? LastErrorMessage, DateTimeOffset? LastErrorDate);
