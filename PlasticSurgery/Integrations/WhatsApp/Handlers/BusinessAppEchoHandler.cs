using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;
using PlasticSurgery.Integrations.WhatsApp.Models;
using PlasticSurgery.Services;

namespace PlasticSurgery.Integrations.WhatsApp.Handlers;

/// <summary>
/// Handles ParsedMetaEventKind.BusinessAppEcho — staff replied to a customer directly from the
/// WhatsApp Business App (coexistence) rather than the dashboard. Same lead/conversation resolution
/// as CustomerMessageHandler (Meta's "contacts[]" identifies the customer either way — see
/// MetaWebhookParser), but a different IngestEventType: IMessageService.IngestAsync forces the
/// conversation to human mode and never treats this as AI-eligible.
/// </summary>
public class BusinessAppEchoHandler
{
    private readonly ILeadService _leads;
    private readonly IConversationService _conversations;
    private readonly IMessageService _messages;

    public BusinessAppEchoHandler(ILeadService leads, IConversationService conversations, IMessageService messages)
    {
        _leads = leads;
        _conversations = conversations;
        _messages = messages;
    }

    public async Task<WhatsAppWebhookResponse> HandleAsync(ParsedMetaEvent evt, ResolvedClinic clinic, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(evt.CustomerWaId))
        {
            return new WhatsAppWebhookResponse(true, "business_app_echo", false, clinic.ClinicId, clinic.ChannelIntegrationId);
        }

        var phone = "+" + evt.CustomerWaId.TrimStart('+');
        var lead = await _leads.GetOrCreateByPhoneAsync(clinic.ClinicId, phone, evt.CustomerName, ct);
        var conversation = await _conversations.GetOrCreateForLeadAsync(clinic.ClinicId, lead.Id, ConversationChannel.WhatsApp, ct);

        var result = await _messages.IngestAsync(new IngestMessageRequest(
            clinic.ClinicId, conversation.Id, lead.Id, IngestEventType.BusinessAppEcho, ConversationChannel.WhatsApp,
            evt.Content, evt.ExternalMessageId, DeliveryStatus: null, SentAt: evt.Timestamp, ReceivedAt: null,
            MessageType: evt.MessageType, MetadataJson: evt.MetadataJson), ct);

        return new WhatsAppWebhookResponse(
            Processed: true, EventType: "business_app_echo", ShouldRunAi: false,
            ClinicId: clinic.ClinicId, WhatsAppConnectionId: clinic.ChannelIntegrationId,
            ConversationId: conversation.Id, LeadId: lead.Id, MessageId: result.Message?.Id,
            ExternalMessageId: evt.ExternalMessageId, Mode: result.ConversationMode);
    }
}
