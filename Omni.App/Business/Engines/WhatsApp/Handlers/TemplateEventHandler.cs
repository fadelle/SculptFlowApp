using Omni.App.Business.Contracts.Services.WhatsApp;
using Omni.App.Entities.Dtos.WhatsApp;
using Omni.App.Entities.Requests.WhatsApp;
using Omni.App.Entities.Responses.WhatsApp;

namespace Omni.App.Business.Engines.WhatsApp.Handlers;

/// <summary>Handles ParsedMetaEventKind.TemplateStatus — reuses
/// IWhatsAppTemplateService.ApplyMetaEventAsync exactly as the earlier normalized templates/events
/// endpoint did (upsert by MetaTemplateId, falling back to Name+Language, broadcasts
/// WhatsAppTemplateUpdated). Never AI-eligible.</summary>
public class TemplateEventHandler
{
    private readonly IWhatsAppTemplateService _templates;

    public TemplateEventHandler(IWhatsAppTemplateService templates)
    {
        _templates = templates;
    }

    public async Task<WhatsAppWebhookResponse> HandleAsync(ParsedMetaEvent evt, ResolvedClient client, CancellationToken ct)
    {
        var template = await _templates.ApplyMetaEventAsync(new WhatsAppTemplateEventRequest(
            client.ClientId, evt.RawFieldName ?? "template_status_update", evt.WabaId, evt.MetaTemplateId,
            evt.TemplateName ?? "unknown", evt.TemplateLanguage, evt.TemplateCategory, evt.TemplateStatus, evt.TemplateReason,
            evt.TemplateQualityRating, evt.TemplatePreviousCategory, evt.TemplateCurrentCategory,
            evt.TemplateComponentsJson, evt.Timestamp), ct);

        return new WhatsAppWebhookResponse(
            Processed: true, EventType: "template_status", ShouldRunAi: false,
            ClientId: client.ClientId, WhatsAppConnectionId: client.ChannelIntegrationId,
            TemplateId: template.Id, MetaTemplateId: template.MetaTemplateId, TemplateName: template.Name, Status: template.Status);
    }
}
