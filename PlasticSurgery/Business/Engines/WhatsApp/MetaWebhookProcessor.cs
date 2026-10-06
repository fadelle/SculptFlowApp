using System.Text.Json;
using PlasticSurgery.Business.Contracts.Engines.WhatsApp;
using PlasticSurgery.Business.Engines.WhatsApp.Handlers;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.WhatsApp;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.WhatsApp;
using PlasticSurgery.Persistence.Contracts.Channels;

namespace PlasticSurgery.Business.Engines.WhatsApp;

public class MetaWebhookProcessor : IMetaWebhookProcessor
{
    private readonly IChannelIntegrationRepository _integrations;
    private readonly CustomerMessageHandler _customerMessageHandler;
    private readonly BusinessAppEchoHandler _businessAppEchoHandler;
    private readonly MessageStatusHandler _messageStatusHandler;
    private readonly TemplateEventHandler _templateEventHandler;
    private readonly HealthEventHandler _healthEventHandler;
    private readonly HistoryHandler _historyHandler;
    private readonly AppStateSyncHandler _appStateSyncHandler;
    private readonly UnknownEventHandler _unknownEventHandler;

    public MetaWebhookProcessor(
        IChannelIntegrationRepository integrations,
        CustomerMessageHandler customerMessageHandler,
        BusinessAppEchoHandler businessAppEchoHandler,
        MessageStatusHandler messageStatusHandler,
        TemplateEventHandler templateEventHandler,
        HealthEventHandler healthEventHandler,
        HistoryHandler historyHandler,
        AppStateSyncHandler appStateSyncHandler,
        UnknownEventHandler unknownEventHandler)
    {
        _integrations = integrations;
        _customerMessageHandler = customerMessageHandler;
        _businessAppEchoHandler = businessAppEchoHandler;
        _messageStatusHandler = messageStatusHandler;
        _templateEventHandler = templateEventHandler;
        _healthEventHandler = healthEventHandler;
        _historyHandler = historyHandler;
        _appStateSyncHandler = appStateSyncHandler;
        _unknownEventHandler = unknownEventHandler;
    }

    public async Task<WhatsAppWebhookResponse> ProcessAsync(JsonElement root, CancellationToken ct = default)
    {
        var parsedEvents = MetaWebhookParser.Parse(root);
        if (parsedEvents.Count == 0)
        {
            return new WhatsAppWebhookResponse(true, "unknown", false, null);
        }

        var results = new List<WhatsAppWebhookResponse>(parsedEvents.Count);
        foreach (var evt in parsedEvents)
        {
            var clinic = await ResolveClinicAsync(evt, ct);
            if (clinic is null)
            {
                // Can't attribute this event to a clinic we know about — acknowledge without
                // guessing. Common right after a webhook is first configured, before any clinic has
                // connected the matching phone number/WABA yet.
                results.Add(await _unknownEventHandler.HandleAsync(evt, null, ct));
                continue;
            }

            var response = evt.Kind switch
            {
                ParsedMetaEventKind.CustomerMessage => await _customerMessageHandler.HandleAsync(evt, clinic, ct),
                ParsedMetaEventKind.BusinessAppEcho => await _businessAppEchoHandler.HandleAsync(evt, clinic, ct),
                ParsedMetaEventKind.MessageStatus => await _messageStatusHandler.HandleAsync(evt, clinic, ct),
                ParsedMetaEventKind.TemplateStatus => await _templateEventHandler.HandleAsync(evt, clinic, ct),
                ParsedMetaEventKind.WhatsAppHealth => await _healthEventHandler.HandleAsync(evt, clinic, ct),
                ParsedMetaEventKind.HistorySync => await _historyHandler.HandleAsync(evt, clinic, ct),
                ParsedMetaEventKind.AppStateSync => await _appStateSyncHandler.HandleAsync(evt, clinic, ct),
                _ => await _unknownEventHandler.HandleAsync(evt, clinic, ct)
            };
            results.Add(response);
        }

        // Multiple events in one payload: every one was already persisted/broadcast above via its
        // own handler call. This single return value is only what n8n needs to decide whether to
        // run the AI — an AI-eligible customer message wins if any exists; otherwise the most
        // recently processed event, so there's still something coherent to report.
        return results.FirstOrDefault(r => r.ShouldRunAi) ?? results[^1];
    }

    /// <summary>Resolves the trusted clinic for a parsed event from its own stored WhatsApp
    /// connection — PhoneNumberId first (the more specific match, and present on most event
    /// kinds), falling back to WabaId. This is the only place in the whole feature that decides
    /// "which clinic does this belong to"; nothing downstream re-derives it from the payload.</summary>
    private async Task<ResolvedClinic?> ResolveClinicAsync(ParsedMetaEvent evt, CancellationToken ct)
    {
        ChannelIntegration? integration = null;
        if (!string.IsNullOrWhiteSpace(evt.PhoneNumberId))
        {
            integration = await _integrations.FindWhatsAppByPhoneNumberIdAsync(evt.PhoneNumberId, ct);
        }
        if (integration is null && !string.IsNullOrWhiteSpace(evt.WabaId))
        {
            integration = await _integrations.FindWhatsAppByWabaIdAsync(evt.WabaId, ct);
        }

        return integration is null ? null : new ResolvedClinic(integration.ClinicId, integration.Id);
    }
}
