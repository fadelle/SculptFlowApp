using System.Text.Json;
using System.Text.Json.Serialization;
using PlasticSurgery.Entities.Dtos.Ai;

namespace PlasticSurgery.Business.Contracts.HttpClients.N8n;

/// <summary>
/// The single outbound call to n8n now that Meta posts directly to our webhook instead of n8n
/// relaying the raw payload (see Controllers/Integrations/WhatsAppWebhookController.cs). n8n no longer sees Meta's
/// webhook shape at all — it receives only this small normalized trigger, and only for the one case
/// that's ever allowed to reach the AI: an eligible customer message in a conversation currently in
/// AI mode (MetaWebhookProcessor/WhatsAppWebhookResponse.ShouldRunAi already decided that).
/// </summary>
public interface IAiTriggerNotifier
{
    Task NotifyAsync(AiTriggerPayload payload, CancellationToken ct = default);
}
