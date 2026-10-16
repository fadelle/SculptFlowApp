using Omni.App.Business.Contracts.Services.WhatsApp;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.WhatsApp;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.WhatsApp;
using Omni.App.Entities.Responses.WhatsApp;

namespace Omni.App.Business.Engines.WhatsApp.Handlers;

/// <summary>Handles ParsedMetaEventKind.WhatsAppHealth (phone quality/name, account, account
/// review, connection updates) — reuses IWhatsAppHealthService.ApplyHealthEventAsync exactly as the
/// earlier normalized health/events endpoint did. Never AI-eligible.</summary>
public class HealthEventHandler
{
    private readonly IWhatsAppHealthService _health;

    public HealthEventHandler(IWhatsAppHealthService health)
    {
        _health = health;
    }

    public async Task<WhatsAppWebhookResponse> HandleAsync(ParsedMetaEvent evt, ResolvedClient client, CancellationToken ct)
    {
        var health = await _health.ApplyHealthEventAsync(new WhatsAppHealthEventRequest(
            client.ClientId, evt.HealthEventType ?? WhatsAppHealthEventType.UnknownMetaHealthEvent, evt.WabaId, evt.PhoneNumberId,
            evt.HealthStatus, evt.HealthQualityRating, evt.HealthCode, evt.HealthMessage, evt.Timestamp, evt.RawJson), ct);

        return new WhatsAppWebhookResponse(
            Processed: true, EventType: "whatsapp_health", ShouldRunAi: false,
            ClientId: client.ClientId, WhatsAppConnectionId: client.ChannelIntegrationId,
            HealthLevel: health.HealthLevel, HealthEventType: evt.HealthEventType, Status: evt.HealthStatus, Message: evt.HealthMessage);
    }
}
