using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;
using PlasticSurgery.Integrations.WhatsApp.Handlers;
using PlasticSurgery.Integrations.WhatsApp.Models;

namespace PlasticSurgery.Integrations.Infobip;

/// <summary>
/// Infobip counterpart of MetaWebhookProcessor. The clinic is never read from the payload: it's the stored
/// connection InfobipWhatsAppWebhookController already authenticated (connectionId in the URL + its secret).
/// Each parsed event goes to the SAME handler Meta events use, so lead/conversation lookup, message storage,
/// the 24h window, campaign reply tracking, SignalR and notifications all stay in one place:
///
///   customer message -> CustomerMessageHandler -> ILeadService/IConversationService/IMessageService.IngestAsync
///   delivered/read/failed -> MessageStatusHandler -> IMessageService.IngestAsync(status_update)
///   anything else    -> UnknownEventHandler (logged as WHATSAPP_UNKNOWN_EVENT_RECEIVED)
///
/// Idempotency: Infobip retries a webhook until it gets a 2xx, so the same result can arrive twice. Inbound
/// messages are deduplicated on (clinic, channel, external message id) by IngestAsync plus the
/// ux_messages_clinic_channel_external_message_id unique index; repeated status reports are no-ops there too.
/// </summary>
public interface IInfobipWhatsAppWebhookProcessor
{
    /// <summary>One response per result in the body (Infobip batches several results into one POST).</summary>
    Task<IReadOnlyList<WhatsAppWebhookResponse>> ProcessAsync(ChannelIntegration connection, JsonElement root, CancellationToken ct = default);

    /// <summary>Account-level template update (status / category / quality). Infobip sends these for the whole
    /// account, not per sender, so the clinic is found from the template itself: the Infobip-submitted template
    /// with that id. Null when the body isn't a template update or the template isn't one of ours.</summary>
    Task<WhatsAppWebhookResponse?> ProcessTemplateUpdateAsync(JsonElement root, CancellationToken ct = default);
}

public class InfobipWhatsAppWebhookProcessor : IInfobipWhatsAppWebhookProcessor
{
    private readonly ApplicationDbContext _db;
    private readonly CustomerMessageHandler _customerMessageHandler;
    private readonly MessageStatusHandler _messageStatusHandler;
    private readonly TemplateEventHandler _templateEventHandler;
    private readonly UnknownEventHandler _unknownEventHandler;
    private readonly ILogger<InfobipWhatsAppWebhookProcessor> _logger;

    public InfobipWhatsAppWebhookProcessor(
        ApplicationDbContext db,
        CustomerMessageHandler customerMessageHandler,
        MessageStatusHandler messageStatusHandler,
        TemplateEventHandler templateEventHandler,
        UnknownEventHandler unknownEventHandler,
        ILogger<InfobipWhatsAppWebhookProcessor> logger)
    {
        _db = db;
        _customerMessageHandler = customerMessageHandler;
        _messageStatusHandler = messageStatusHandler;
        _templateEventHandler = templateEventHandler;
        _unknownEventHandler = unknownEventHandler;
        _logger = logger;
    }

    public async Task<IReadOnlyList<WhatsAppWebhookResponse>> ProcessAsync(ChannelIntegration connection, JsonElement root, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await _db.ChannelIntegrations.Where(c => c.Id == connection.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastWebhookAt, now), ct);

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

        var owner = await _db.WhatsAppTemplates.AsNoTracking()
            .Where(t => t.MetaTemplateId == evt.MetaTemplateId && t.Provider == ChannelProvider.Infobip)
            .Select(t => new { t.ClinicId })
            .FirstOrDefaultAsync(ct);
        if (owner is null)
        {
            // Created outside SculptFlow (e.g. in the Infobip portal) — no clinic to attribute it to.
            _logger.LogInformation("Infobip template update for unknown template {TemplateId}; ignored.", evt.MetaTemplateId);
            return null;
        }

        var integrationId = await _db.ChannelIntegrations.AsNoTracking()
            .Where(c => c.ClinicId == owner.ClinicId && c.Channel == ChannelType.WhatsApp)
            .Select(c => c.Id)
            .FirstOrDefaultAsync(ct);

        return await _templateEventHandler.HandleAsync(evt, new ResolvedClinic(owner.ClinicId, integrationId), ct);
    }
}
