using PlasticSurgery.Entities.Responses.Channels;

namespace PlasticSurgery.Business.Contracts.Services.Channels;

/// <summary>
/// Connects a clinic's WhatsApp to its sender number on SculptFlow's Infobip account. There's no Embedded
/// Signup on this path: the number is registered as a WhatsApp sender in the Infobip portal first (by
/// SculptFlow), then the clinic (or SculptFlow on its behalf) enters it in Settings → Messaging Integrations.
///
/// Connect:
///   1. Infobip must be the active provider (WhatsApp:Provider = infobip) and configured (Infobip:BaseUrl/ApiKey)
///   2. GET business-info proves the number is a WhatsApp sender on our Infobip account
///   3. the number may be connected to only one clinic (also enforced by ux_channel_integrations_provider_sender)
///   4. upsert the clinic's single WhatsApp row: provider=infobip, provider_sender_id, a per-connection webhook
///      secret, status=connected. Meta-only fields are cleared so Meta webhooks can no longer route to it.
/// Disconnect is the generic ChannelIntegrationService.DisconnectAsync (status=disconnected, secret dropped,
/// so the webhook URL stops working at once).
/// </summary>
public interface IInfobipWhatsAppIntegrationService
{
    /// <summary>Throws ArgumentException (malformed number) or InvalidOperationException (Infobip not active or
    /// configured, number not on our Infobip account, number used by another clinic, Infobip unreachable).
    /// Messages are shown to clinic staff, so they never name Infobip or config keys (details go to the log).</summary>
    Task<ChannelIntegrationResponse> ConnectAsync(Guid clinicId, string? senderNumber, CancellationToken ct = default);

    /// <summary>The clinic's webhook URL to paste into the Infobip portal (contains the secret — show it only to
    /// the clinic's own signed-in staff, never in an API response). Null when not connected through Infobip.</summary>
    Task<string?> GetWebhookUrlAsync(Guid clinicId, CancellationToken ct = default);
}
