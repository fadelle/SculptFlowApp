using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;
using PlasticSurgery.Integrations.WhatsApp.Models;
using PlasticSurgery.Services;

namespace PlasticSurgery.Integrations.WhatsApp.Handlers;

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

    public async Task<WhatsAppWebhookResponse> HandleAsync(ParsedMetaEvent evt, ResolvedClinic clinic, CancellationToken ct)
    {
        var health = await _health.ApplyHealthEventAsync(new WhatsAppHealthEventRequest(
            clinic.ClinicId, evt.HealthEventType ?? WhatsAppHealthEventType.UnknownMetaHealthEvent, evt.WabaId, evt.PhoneNumberId,
            evt.HealthStatus, evt.HealthQualityRating, evt.HealthCode, evt.HealthMessage, evt.Timestamp, evt.RawJson), ct);

        return new WhatsAppWebhookResponse(
            Processed: true, EventType: "whatsapp_health", ShouldRunAi: false,
            ClinicId: clinic.ClinicId, WhatsAppConnectionId: clinic.ChannelIntegrationId,
            HealthLevel: health.HealthLevel, HealthEventType: evt.HealthEventType, Status: evt.HealthStatus, Message: evt.HealthMessage);
    }
}
