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
using Omni.App.Common.Helpers;
using Omni.App.Persistence.Contracts;
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
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<MetaWebhookProcessor> _logger;

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
        IMessageTracer tracer,
        IUnitOfWork unitOfWork,
        ILogger<MetaWebhookProcessor> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
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
        var parsedEvents = MetaWebhookParser.Parse(root, out var unreadable);
        if (unreadable > 0)
        {
            _tracer.Step(TraceSteps.WebhookIgnored, TraceOutcomes.Skipped, new TraceIds(Channel: ConversationChannel.WhatsApp),
                detail: new { reason = "unreadable_parts", count = unreadable });
        }
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
            ResolvedClient? client = null;
            try
            {
                var (resolved, unresolvedReason) = await ResolveClientAsync(evt, ct);
                client = resolved;
                if (client is null)
                {
                    _tracer.Step(TraceSteps.WebhookClientUnresolved, TraceOutcomes.Skipped,
                        new TraceIds(Channel: ConversationChannel.WhatsApp, ExternalMessageId: evt.ExternalMessageId),
                        detail: new { kind = evt.Kind.ToString(), reason = unresolvedReason });
                }
                results.Add(await HandleAsync(evt, client, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad event never fails the rest of the delivery (QA t10-03): the customer messages around it are still
                // saved and answered, and Meta gets its 200 instead of retrying a delivery that would fail the same way.
                _unitOfWork.DiscardChanges();
                _logger.LogError(ex, "WhatsApp webhook: a {Kind} event failed and was skipped.", evt.Kind);
                _tracer.Step(TraceSteps.WebhookEventFailed, TraceOutcomes.Failed,
                    new TraceIds(client?.ClientId, Channel: ConversationChannel.WhatsApp, ExternalMessageId: evt.ExternalMessageId),
                    detail: new { kind = evt.Kind.ToString(), error = TraceText.Error(ex) });
                results.Add(new WhatsAppWebhookResponse(false, evt.Kind.ToString(), false, client?.ClientId));
            }
        }

        // Every event was already persisted/broadcast above via its own handler call.
        return results;
    }

    private async Task<WhatsAppWebhookResponse> HandleAsync(ParsedMetaEvent evt, ResolvedClient? client, CancellationToken ct)
    {
        {
            if (client is null)
            {
                // Can't attribute this event to a client we know about — acknowledge without
                // guessing. Common right after a webhook is first configured, before any client has
                // connected the matching phone number/WABA yet.
                return await _unknownEventHandler.HandleAsync(evt, null, ct);
            }

            return evt.Kind switch
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
        }
    }

    /// <summary>Resolves the trusted client for a parsed event from its own stored WhatsApp connection. This is the only place
    /// in the whole feature that decides "which client does this belong to"; nothing downstream re-derives it from the payload.
    /// Only CONNECTED connections receive anything (QA t10-01):
    /// <list type="bullet">
    /// <item>An event naming a phone number id goes to the connection with that number, and only when the delivery's account id
    /// (if any) is that connection's; never on to the account id.</item>
    /// <item>A message event (customer message, echo, status) without a phone number id goes nowhere.</item>
    /// <item>An account-wide event (template, account update) goes to the one connection with that account id; two or more is
    /// ambiguous and goes nowhere.</item>
    /// </list>
    /// Null with the reason when nothing matches.</summary>
    private async Task<(ResolvedClient? Client, string? Reason)> ResolveClientAsync(ParsedMetaEvent evt, CancellationToken ct)
    {
        // Cached (every incoming message asks this); ChannelIntegrationService drops
        // CacheKeys.WhatsAppRoutingPrefix whenever a connection changes.
        if (!string.IsNullOrWhiteSpace(evt.PhoneNumberId))
        {
            var byPhone = await _cache.GetOrCreateAsync(CacheKeys.WhatsAppByPhoneNumberId(evt.PhoneNumberId), CacheKeys.WhatsAppRoutingTtl,
                async token => ToResolved(await _integrations.ListConnectedWhatsAppByPhoneNumberIdAsync(evt.PhoneNumberId, token)), ct);
            if (byPhone is null) return (null, "unknown_phone_number_id");
            if (!string.IsNullOrWhiteSpace(evt.WabaId) && !string.IsNullOrWhiteSpace(byPhone.WabaId) && byPhone.WabaId != evt.WabaId)
            {
                return (null, "waba_mismatch");
            }
            return (byPhone, null);
        }
        if (evt.Kind is ParsedMetaEventKind.CustomerMessage or ParsedMetaEventKind.BusinessAppEcho or ParsedMetaEventKind.MessageStatus)
        {
            return (null, "no_phone_number_id");
        }
        if (string.IsNullOrWhiteSpace(evt.WabaId)) return (null, "no_ids");

        var byAccount = await _cache.GetOrCreateAsync(CacheKeys.WhatsAppByWabaId(evt.WabaId), CacheKeys.WhatsAppRoutingTtl,
            async token => ToResolved(await _integrations.ListConnectedWhatsAppByWabaIdAsync(evt.WabaId, token)), ct);
        return byAccount is null ? (null, "unknown_or_shared_waba") : (byAccount, null);
    }

    /// <summary>The one connection that matched, or null when none or several did (several = ambiguous, never a guess).</summary>
    private static ResolvedClient? ToResolved(IReadOnlyList<ChannelIntegration> integrations) =>
        integrations.Count == 1 ? new ResolvedClient(integrations[0].ClientId, integrations[0].Id, integrations[0].WhatsAppBusinessId) : null;
}
