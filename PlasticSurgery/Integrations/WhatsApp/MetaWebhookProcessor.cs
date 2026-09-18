using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;
using PlasticSurgery.Integrations.WhatsApp.Handlers;
using PlasticSurgery.Integrations.WhatsApp.Models;

namespace PlasticSurgery.Integrations.WhatsApp;

/// <summary>
/// The single orchestration point for POST /api/integrations/whatsapp/webhook — the only thing
/// Controllers/WhatsAppIntegrationEventsController.cs's Webhook action calls. Parses the raw Meta
/// payload (MetaWebhookParser), resolves the trusted clinic for each contained event from the
/// clinic's own stored WhatsApp connection (never from n8n or the Meta body), dispatches to the
/// matching Handler — each of which reuses the same IMessageService/IWhatsAppTemplateService/
/// IWhatsAppHealthService methods the earlier normalized per-domain endpoints called, so
/// persistence/SignalR logic exists in exactly one place regardless of how the event arrived — and
/// returns the ONE flat response n8n needs (see WhatsAppWebhookResponse's own doc comment for the
/// multi-event selection rule).
/// </summary>
public interface IMetaWebhookProcessor
{
    Task<WhatsAppWebhookResponse> ProcessAsync(JsonElement root, CancellationToken ct = default);
}

public class MetaWebhookProcessor : IMetaWebhookProcessor
{
    private readonly ApplicationDbContext _db;
    private readonly CustomerMessageHandler _customerMessageHandler;
    private readonly BusinessAppEchoHandler _businessAppEchoHandler;
    private readonly MessageStatusHandler _messageStatusHandler;
    private readonly TemplateEventHandler _templateEventHandler;
    private readonly HealthEventHandler _healthEventHandler;
    private readonly HistoryHandler _historyHandler;
    private readonly AppStateSyncHandler _appStateSyncHandler;
    private readonly UnknownEventHandler _unknownEventHandler;

    public MetaWebhookProcessor(
        ApplicationDbContext db,
        CustomerMessageHandler customerMessageHandler,
        BusinessAppEchoHandler businessAppEchoHandler,
        MessageStatusHandler messageStatusHandler,
        TemplateEventHandler templateEventHandler,
        HealthEventHandler healthEventHandler,
        HistoryHandler historyHandler,
        AppStateSyncHandler appStateSyncHandler,
        UnknownEventHandler unknownEventHandler)
    {
        _db = db;
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
            integration = await _db.ChannelIntegrations.FirstOrDefaultAsync(
                c => c.Channel == ChannelType.WhatsApp && c.PhoneNumberId == evt.PhoneNumberId, ct);
        }
        if (integration is null && !string.IsNullOrWhiteSpace(evt.WabaId))
        {
            integration = await _db.ChannelIntegrations.FirstOrDefaultAsync(
                c => c.Channel == ChannelType.WhatsApp && c.WhatsAppBusinessId == evt.WabaId, ct);
        }

        return integration is null ? null : new ResolvedClinic(integration.ClinicId, integration.Id);
    }
}
