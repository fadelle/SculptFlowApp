using System.Text.Json;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.Telegram;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Business.Contracts.Engines.Telegram;

/// <summary>
/// Telegram Update -> normalized SculptFlow records. The Telegram counterpart of MetaWebhookProcessor +
/// CustomerMessageHandler, and just as thin: parse, resolve the lead/conversation, then hand to the
/// existing IMessageService.IngestAsync — which already persists, updates the conversation, broadcasts
/// SignalR and decides AI eligibility. No persistence or SignalR logic is duplicated here.
///
/// The clinic comes from the already-authenticated <see cref="ChannelIntegration"/> (resolved by the
/// controller from the connectionId in the URL and verified against the webhook secret) — never from
/// anything in the Telegram payload.
/// </summary>
public interface ITelegramWebhookProcessor
{
    Task<TelegramWebhookResult> ProcessAsync(ChannelIntegration connection, JsonElement update, CancellationToken ct = default);

    /// <summary>
    /// One Telegram delivery: the connection must be a connected Telegram row and <paramref name="secret"/> (the
    /// X-Telegram-Bot-Api-Secret-Token header) must match its stored secret (constant-time). Notifies n8n only if
    /// the message is AI-eligible.
    /// </summary>
    Task<WebhookReceiveOutcome> ReceiveAsync(Guid connectionId, string? secret, JsonElement update, CancellationToken ct = default);
}
