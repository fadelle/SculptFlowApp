using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;
using PlasticSurgery.Integrations.WhatsApp.Models;
using PlasticSurgery.Services;

namespace PlasticSurgery.Integrations.WhatsApp.Handlers;

/// <summary>
/// Handles ParsedMetaEventKind.CustomerMessage. Meta only ever gives us the customer's WhatsApp id
/// (wa_id) — never our internal LeadId/ConversationId — so this resolves (or creates) both first,
/// then hands off to IMessageService.IngestAsync exactly as the earlier normalized ingest endpoint
/// did. No persistence/SignalR logic is duplicated here; this class is purely the "what do I need
/// to look up before I can call the existing service" adapter.
/// </summary>
public class CustomerMessageHandler
{
    private readonly ILeadService _leads;
    private readonly IConversationService _conversations;
    private readonly IMessageService _messages;

    public CustomerMessageHandler(ILeadService leads, IConversationService conversations, IMessageService messages)
    {
        _leads = leads;
        _conversations = conversations;
        _messages = messages;
    }

    public async Task<WhatsAppWebhookResponse> HandleAsync(ParsedMetaEvent evt, ResolvedClinic clinic, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(evt.CustomerWaId))
        {
            return new WhatsAppWebhookResponse(true, "customer_message", false, clinic.ClinicId, clinic.ChannelIntegrationId);
        }

        // Meta's wa_id is digits only (no leading "+") — normalize to this app's E.164-ish
        // convention ("+<digits>") used elsewhere in Lead.Phone.
        var phone = "+" + evt.CustomerWaId.TrimStart('+');
        var lead = await _leads.GetOrCreateByPhoneAsync(clinic.ClinicId, phone, evt.CustomerName, ct);
        var conversation = await _conversations.GetOrCreateForLeadAsync(clinic.ClinicId, lead.Id, ConversationChannel.WhatsApp, ct);

        var result = await _messages.IngestAsync(new IngestMessageRequest(
            clinic.ClinicId, conversation.Id, lead.Id, IngestEventType.CustomerMessage, ConversationChannel.WhatsApp,
            evt.Content, evt.ExternalMessageId, DeliveryStatus: null, SentAt: null, ReceivedAt: evt.Timestamp,
            MessageType: evt.MessageType, MetadataJson: evt.MetadataJson), ct);

        return new WhatsAppWebhookResponse(
            Processed: true, EventType: "customer_message", ShouldRunAi: result.AiEligible,
            ClinicId: clinic.ClinicId, WhatsAppConnectionId: clinic.ChannelIntegrationId,
            ConversationId: conversation.Id, LeadId: lead.Id, MessageId: result.Message?.Id,
            ExternalMessageId: evt.ExternalMessageId, Mode: result.ConversationMode,
            MessageType: result.Message?.MessageType, Content: result.Message?.Content,
            SelectedValue: evt.SelectedValue);
    }
}
