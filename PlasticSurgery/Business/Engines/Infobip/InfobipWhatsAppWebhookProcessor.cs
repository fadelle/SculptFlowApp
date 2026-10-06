using System.Text.Json;
using PlasticSurgery.Business.Contracts.Engines.Infobip;
using PlasticSurgery.Business.Contracts.HttpClients.N8n;
using PlasticSurgery.Business.Engines.WhatsApp.Handlers;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Helpers;
using PlasticSurgery.Entities.Dtos.Ai;
using PlasticSurgery.Entities.Dtos.WhatsApp;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.WhatsApp;
using PlasticSurgery.Persistence.Contracts.Channels;
using PlasticSurgery.Persistence.Contracts.WhatsApp;

namespace PlasticSurgery.Business.Engines.Infobip;

public class InfobipWhatsAppWebhookProcessor : IInfobipWhatsAppWebhookProcessor
{
    private readonly IChannelIntegrationRepository _integrations;
    private readonly IWhatsAppTemplateRepository _templates;
    private readonly IAiTriggerNotifier _aiTrigger;
    private readonly IConfiguration _configuration;
    private readonly CustomerMessageHandler _customerMessageHandler;
    private readonly MessageStatusHandler _messageStatusHandler;
    private readonly TemplateEventHandler _templateEventHandler;
    private readonly UnknownEventHandler _unknownEventHandler;
    private readonly ILogger<InfobipWhatsAppWebhookProcessor> _logger;

    public InfobipWhatsAppWebhookProcessor(
        IChannelIntegrationRepository integrations,
        IWhatsAppTemplateRepository templates,
        IAiTriggerNotifier aiTrigger,
        IConfiguration configuration,
        CustomerMessageHandler customerMessageHandler,
        MessageStatusHandler messageStatusHandler,
        TemplateEventHandler templateEventHandler,
        UnknownEventHandler unknownEventHandler,
        ILogger<InfobipWhatsAppWebhookProcessor> logger)
    {
        _integrations = integrations;
        _templates = templates;
        _aiTrigger = aiTrigger;
        _configuration = configuration;
        _customerMessageHandler = customerMessageHandler;
        _messageStatusHandler = messageStatusHandler;
        _templateEventHandler = templateEventHandler;
        _unknownEventHandler = unknownEventHandler;
        _logger = logger;
    }

    public async Task<IReadOnlyList<WhatsAppWebhookResponse>> ProcessAsync(ChannelIntegration connection, JsonElement root, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await _integrations.TouchLastWebhookAsync(connection.Id, now, ct);

        var clinic = new ResolvedClinic(connection.ClinicId, connection.Id);
        var events = InfobipWhatsAppWebhookParser.Parse(root);
        var responses = new List<WhatsAppWebhookResponse>(events.Count);

        foreach (var evt in events)
        {
            // An inbound message names the business number it was sent to. If that isn't this connection's
            // sender, the Infobip forwarding for some other number points at this clinic's URL — don't file
            // another number's patient under this clinic.
            if (evt.Kind == ParsedMetaEventKind.CustomerMessage
                && !string.IsNullOrEmpty(evt.ProviderSenderId)
                && evt.ProviderSenderId != connection.ProviderSenderId)
            {
                _logger.LogWarning("Infobip inbound message for a different sender reached connection {ConnectionId}; ignored.", connection.Id);
                responses.Add(await _unknownEventHandler.HandleAsync(new ParsedMetaEvent
                {
                    Kind = ParsedMetaEventKind.Unknown,
                    RawFieldName = "sender_mismatch",
                    RawJson = evt.RawJson
                }, clinic, ct));
                continue;
            }

            var response = evt.Kind switch
            {
                ParsedMetaEventKind.CustomerMessage => await _customerMessageHandler.HandleAsync(evt, clinic, ct),
                ParsedMetaEventKind.MessageStatus when !string.IsNullOrEmpty(evt.ExternalMessageId)
                    => await _messageStatusHandler.HandleAsync(evt, clinic, ct),
                _ => await _unknownEventHandler.HandleAsync(evt, clinic, ct)
            };

            if (evt.Kind == ParsedMetaEventKind.MessageStatus && !response.Processed)
            {
                // A report for a message we have no row for (sent before this connection existed, or by another
                // tool on the same Infobip account). Nothing to update — acknowledged so Infobip stops retrying.
                _logger.LogInformation("Infobip status report for unknown message {ExternalMessageId} on connection {ConnectionId}.",
                    evt.ExternalMessageId, connection.Id);
            }

            responses.Add(response);
        }

        return responses;
    }

    public async Task<WhatsAppWebhookResponse?> ProcessTemplateUpdateAsync(JsonElement root, CancellationToken ct = default)
    {
        var evt = InfobipWhatsAppWebhookParser.ParseTemplateUpdate(root);
        if (evt is null)
        {
            _logger.LogInformation("Infobip account webhook body wasn't a template update; ignored.");
            return null;
        }

        var ownerClinicId = await _templates.FindInfobipTemplateClinicIdAsync(evt.MetaTemplateId, ct);
        if (ownerClinicId is not { } clinicId)
        {
            // Created outside SculptFlow (e.g. in the Infobip portal) — no clinic to attribute it to.
            _logger.LogInformation("Infobip template update for unknown template {TemplateId}; ignored.", evt.MetaTemplateId);
            return null;
        }

        var integrationId = await _integrations.GetIdAsync(clinicId, ChannelType.WhatsApp, ct);

        return await _templateEventHandler.HandleAsync(evt, new ResolvedClinic(clinicId, integrationId), ct);
    }

    public async Task<WebhookReceiveOutcome> ReceiveAsync(Guid connectionId, string? token, JsonElement body,
        CancellationToken ct = default)
    {
        var connection = await _integrations.GetByIdReadOnlyAsync(connectionId, ct);

        if (connection is null
            || connection.Channel != ChannelType.WhatsApp
            || ChannelProvider.Of(connection) != ChannelProvider.Infobip
            || connection.Status != ChannelIntegrationStatus.Connected)
        {
            return WebhookReceiveOutcome.NotFound;
        }

        // Same constant-time compare the Telegram webhook uses for its per-connection secret.
        if (!TelegramWebhookSecret.Matches(connection.WebhookVerifyToken, token))
        {
            _logger.LogWarning("Infobip webhook token mismatch for connection {ConnectionId}.", connectionId);
            return WebhookReceiveOutcome.Forbidden;
        }

        // A template update posted to a clinic's URL (if the account-level subscription points here) is handled
        // like on the account endpoint — it's matched to its clinic by template id, never by this URL.
        if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("messageTemplateId", out _))
        {
            await ProcessTemplateUpdateAsync(body, ct);
            return WebhookReceiveOutcome.Accepted;
        }

        var results = await ProcessAsync(connection, body, ct);

        // One AI turn per conversation per delivery: if a batch holds several messages from the same patient,
        // the AI answers once, after the last of them (it reads the whole conversation anyway).
        var aiTriggers = results
            .Where(r => r.ShouldRunAi && r.ClinicId.HasValue && r.ConversationId.HasValue)
            .GroupBy(r => r.ConversationId!.Value)
            .Select(g => g.Last());

        foreach (var r in aiTriggers)
        {
            await _aiTrigger.NotifyAsync(new AiTriggerPayload(
                r.ClinicId!.Value, r.ConversationId!.Value, r.LeadId, r.MessageId,
                Channel: ConversationChannel.WhatsApp, MessageType: r.MessageType, MessageText: r.Content,
                SelectedValue: r.SelectedValue), ct);
        }

        return WebhookReceiveOutcome.Accepted;
    }

    public async Task<WebhookReceiveOutcome> ReceiveAccountEventAsync(string? token, JsonElement body, CancellationToken ct = default)
    {
        var expected = _configuration["Infobip:WebhookToken"];
        if (string.IsNullOrEmpty(expected))
        {
            return WebhookReceiveOutcome.NotFound;
        }
        if (!TelegramWebhookSecret.Matches(expected, token))
        {
            _logger.LogWarning("Infobip account webhook token mismatch.");
            return WebhookReceiveOutcome.Forbidden;
        }

        await ProcessTemplateUpdateAsync(body, ct);
        return WebhookReceiveOutcome.Accepted;
    }
}
