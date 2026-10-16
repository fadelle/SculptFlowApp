using System.Text.Json;
using Omni.App.Business.Contracts.Engines.WhatsApp;
using Omni.App.Business.Engines.WhatsApp.Handlers;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Monitoring;
using Omni.App.Entities.Dtos.WhatsApp;
using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.WhatsApp;
using Omni.App.Persistence.Contracts.Channels;

namespace Omni.App.Business.Engines.WhatsApp;

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
    private readonly ICacheManager _cache;
    private readonly IMessageTracer _tracer;

    public MetaWebhookProcessor(
        IChannelIntegrationRepository integrations,
        ICacheManager cache,
        CustomerMessageHandler customerMessageHandler,
        BusinessAppEchoHandler businessAppEchoHandler,
        MessageStatusHandler messageStatusHandler,
        TemplateEventHandler templateEventHandler,
        HealthEventHandler healthEventHandler,
        HistoryHandler historyHandler,
        AppStateSyncHandler appStateSyncHandler,
        UnknownEventHandler unknownEventHandler,
        IMessageTracer tracer)
    {
        _tracer = tracer;
        _integrations = integrations;
        _cache = cache;
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
        var results = await ProcessAllAsync(root, ct);
        // One summary for callers that can only act on a single event: an AI-eligible customer message wins if any exists,
        // otherwise the most recently processed event.
        return results.FirstOrDefault(r => r.ShouldRunAi) ?? results[^1];
    }

    public async Task<IReadOnlyList<WhatsAppWebhookResponse>> ProcessAllAsync(JsonElement root, CancellationToken ct = default)
    {
        var parsedEvents = MetaWebhookParser.Parse(root);
        if (parsedEvents.Count == 0)
        {
            _tracer.Step(TraceSteps.WebhookIgnored, TraceOutcomes.Skipped, new TraceIds(Channel: ConversationChannel.WhatsApp),
                detail: new { reason = "no_events" });
            return [new WhatsAppWebhookResponse(true, "unknown", false, null)];
        }
        // Delivery statuses alone aren't a step: the trace view reads them from the message they update.
        if (parsedEvents.Any(e => e.Kind != ParsedMetaEventKind.MessageStatus))
        {
            _tracer.Step(TraceSteps.WebhookReceived, TraceOutcomes.Ok, new TraceIds(Channel: ConversationChannel.WhatsApp),
                detail: new { events = parsedEvents.Count, kinds = string.Join(",", parsedEvents.Select(e => e.Kind.ToString()).Distinct()) });
        }

        var results = new List<WhatsAppWebhookResponse>(parsedEvents.Count);
        foreach (var evt in parsedEvents)
        {
            var client = await ResolveClientAsync(evt, ct);
            if (client is null)
            {
                _tracer.Step(TraceSteps.WebhookClientUnresolved, TraceOutcomes.Skipped,
                    new TraceIds(Channel: ConversationChannel.WhatsApp, ExternalMessageId: evt.ExternalMessageId), detail: new { kind = evt.Kind.ToString() });
                // Can't attribute this event to a client we know about — acknowledge without
                // guessing. Common right after a webhook is first configured, before any client has
                // connected the matching phone number/WABA yet.
                results.Add(await _unknownEventHandler.HandleAsync(evt, null, ct));
                continue;
            }

            var response = evt.Kind switch
            {
                ParsedMetaEventKind.CustomerMessage => await _customerMessageHandler.HandleAsync(evt, client, ct),
                ParsedMetaEventKind.BusinessAppEcho => await _businessAppEchoHandler.HandleAsync(evt, client, ct),
                ParsedMetaEventKind.MessageStatus => await _messageStatusHandler.HandleAsync(evt, client, ct),
                ParsedMetaEventKind.TemplateStatus => await _templateEventHandler.HandleAsync(evt, client, ct),
                ParsedMetaEventKind.WhatsAppHealth => await _healthEventHandler.HandleAsync(evt, client, ct),
                ParsedMetaEventKind.HistorySync => await _historyHandler.HandleAsync(evt, client, ct),
                ParsedMetaEventKind.AppStateSync => await _appStateSyncHandler.HandleAsync(evt, client, ct),
                _ => await _unknownEventHandler.HandleAsync(evt, client, ct)
            };
            results.Add(response);
        }

        // Every event was already persisted/broadcast above via its own handler call.
        return results;
    }

    /// <summary>Resolves the trusted client for a parsed event from its own stored WhatsApp
    /// connection — PhoneNumberId first (the more specific match, and present on most event
    /// kinds), falling back to WabaId. This is the only place in the whole feature that decides
    /// "which client does this belong to"; nothing downstream re-derives it from the payload.</summary>
    private async Task<ResolvedClient?> ResolveClientAsync(ParsedMetaEvent evt, CancellationToken ct)
    {
        // Cached (every incoming message asks this); ChannelIntegrationService drops
        // CacheKeys.WhatsAppRoutingPrefix whenever a connection changes.
        ResolvedClient? resolved = null;
        if (!string.IsNullOrWhiteSpace(evt.PhoneNumberId))
        {
            resolved = await _cache.GetOrCreateAsync(CacheKeys.WhatsAppByPhoneNumberId(evt.PhoneNumberId), CacheKeys.WhatsAppRoutingTtl,
                async token => ToResolved(await _integrations.FindWhatsAppByPhoneNumberIdAsync(evt.PhoneNumberId, token)), ct);
        }
        if (resolved is null && !string.IsNullOrWhiteSpace(evt.WabaId))
        {
            resolved = await _cache.GetOrCreateAsync(CacheKeys.WhatsAppByWabaId(evt.WabaId), CacheKeys.WhatsAppRoutingTtl,
                async token => ToResolved(await _integrations.FindWhatsAppByWabaIdAsync(evt.WabaId, token)), ct);
        }
        return resolved;
    }

    private static ResolvedClient? ToResolved(ChannelIntegration? integration) =>
        integration is null ? null : new ResolvedClient(integration.ClientId, integration.Id);
}
