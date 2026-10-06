using System.Text.Json;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.WhatsApp;

namespace PlasticSurgery.Business.Contracts.Engines.Infobip;

/// <summary>
/// Infobip counterpart of MetaWebhookProcessor. The clinic is never read from the payload: it's the stored
/// connection InfobipWhatsAppWebhookController already authenticated (connectionId in the URL + its secret).
/// Each parsed event goes to the SAME handler Meta events use, so lead/conversation lookup, message storage,
/// the 24h window, campaign reply tracking, SignalR and notifications all stay in one place:
///
///   customer message -> CustomerMessageHandler -> ILeadService/IConversationService/IMessageService.IngestAsync
///   delivered/read/failed -> MessageStatusHandler -> IMessageService.IngestAsync(status_update)
///   anything else    -> UnknownEventHandler (logged as WHATSAPP_UNKNOWN_EVENT_RECEIVED)
///
/// Idempotency: Infobip retries a webhook until it gets a 2xx, so the same result can arrive twice. Inbound
/// messages are deduplicated on (clinic, channel, external message id) by IngestAsync plus the
/// ux_messages_clinic_channel_external_message_id unique index; repeated status reports are no-ops there too.
/// </summary>
public interface IInfobipWhatsAppWebhookProcessor
{
    /// <summary>One response per result in the body (Infobip batches several results into one POST).</summary>
    Task<IReadOnlyList<WhatsAppWebhookResponse>> ProcessAsync(ChannelIntegration connection, JsonElement root, CancellationToken ct = default);

    /// <summary>Account-level template update (status / category / quality). Infobip sends these for the whole
    /// account, not per sender, so the clinic is found from the template itself: the Infobip-submitted template
    /// with that id. Null when the body isn't a template update or the template isn't one of ours.</summary>
    Task<WhatsAppWebhookResponse?> ProcessTemplateUpdateAsync(JsonElement root, CancellationToken ct = default);

    /// <summary>
    /// A delivery to one clinic's sender URL: the connection must be a connected Infobip WhatsApp row and the token
    /// must match its stored secret (constant-time). The clinic comes from that stored row, never from the payload.
    /// Notifies n8n once per AI-eligible conversation.
    /// </summary>
    Task<WebhookReceiveOutcome> ReceiveAsync(Guid connectionId, string? token, JsonElement body, CancellationToken ct = default);

    /// <summary>An account-level delivery, authenticated by Infobip:WebhookToken (off — NotFound — until configured).</summary>
    Task<WebhookReceiveOutcome> ReceiveAccountEventAsync(string? token, JsonElement body, CancellationToken ct = default);
}
