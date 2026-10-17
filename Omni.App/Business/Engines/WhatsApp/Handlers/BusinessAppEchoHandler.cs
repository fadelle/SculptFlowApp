using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Business.Contracts.Services.Contacts;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.WhatsApp;
using Omni.App.Entities.Requests.Inbox;
using Omni.App.Entities.Responses.WhatsApp;

namespace Omni.App.Business.Engines.WhatsApp.Handlers;

/// <summary>
/// Handles ParsedMetaEventKind.BusinessAppEcho — members replied to a customer directly from the
/// WhatsApp Business App (coexistence) rather than the dashboard. Same contact/conversation resolution
/// as CustomerMessageHandler (Meta's "contacts[]" identifies the customer either way — see
/// MetaWebhookParser), but a different IngestEventType: IMessageService.IngestAsync forces the
/// conversation to human mode and never treats this as AI-eligible.
/// </summary>
public class BusinessAppEchoHandler
{
    private readonly IContactService _contacts;
    private readonly IConversationService _conversations;
    private readonly IMessageService _messages;

    public BusinessAppEchoHandler(IContactService contacts, IConversationService conversations, IMessageService messages)
    {
        _contacts = contacts;
        _conversations = conversations;
        _messages = messages;
    }

    public async Task<WhatsAppWebhookResponse> HandleAsync(ParsedMetaEvent evt, ResolvedClient client, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(evt.CustomerWaId))
        {
            return new WhatsAppWebhookResponse(true, "business_app_echo", false, client.ClientId, client.ChannelIntegrationId);
        }

        var phone = "+" + evt.CustomerWaId.TrimStart('+');
        // Not a NEW_CONTACT candidate even if this creates the contact row — it's member replying, not the
        // contact's own first inbound message, so WasCreated is deliberately not passed through.
        var (contact, _) = await _contacts.GetOrCreateByPhoneAsync(client.ClientId, phone, evt.CustomerName, ct);
        var conversation = await _conversations.GetOrCreateForContactAsync(client.ClientId, contact.Id, ConversationChannel.WhatsApp, ct);

        var result = await _messages.IngestAsync(new IngestMessageRequest(
            client.ClientId, conversation.Id, contact.Id, IngestEventType.BusinessAppEcho, ConversationChannel.WhatsApp,
            evt.Content, evt.ExternalMessageId, DeliveryStatus: null, SentAt: evt.Timestamp, ReceivedAt: null,
            MessageType: evt.MessageType, MetadataJson: evt.MetadataJson), ct);

        return new WhatsAppWebhookResponse(
            Processed: true, EventType: "business_app_echo", ShouldRunAi: false,
            ClientId: client.ClientId, WhatsAppConnectionId: client.ChannelIntegrationId,
            ConversationId: conversation.Id, ContactId: contact.Id, MessageId: result.Message?.Id,
            ExternalMessageId: evt.ExternalMessageId, Mode: result.ConversationMode);
    }
}
