using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Business.Contracts.Services.Contacts;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.WhatsApp;
using Omni.App.Entities.Requests.Inbox;
using Omni.App.Entities.Responses.WhatsApp;

namespace Omni.App.Business.Engines.WhatsApp.Handlers;

/// <summary>
/// Handles ParsedMetaEventKind.BusinessAppEcho — members replied to a customer directly from the
/// WhatsApp Business App (coexistence) rather than the dashboard. Confirmed 2026-11-05 (QA t10-04): the reply is
/// saved in the customer's conversation as the member's outbound message and the conversation goes to human mode,
/// so the AI steps back and the Inbox shows the whole conversation. The customer is the echo's "to" (or the listed
/// contact), never the business's own "from" (MetaWebhookParser); without one nothing is saved. IngestAsync never
/// treats an echo as AI-eligible.
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
