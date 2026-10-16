using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Business.Contracts.Services.Contacts;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.WhatsApp;
using Omni.App.Entities.Requests.Inbox;
using Omni.App.Entities.Responses.WhatsApp;

namespace Omni.App.Business.Engines.WhatsApp.Handlers;

/// <summary>
/// Handles ParsedMetaEventKind.CustomerMessage. Meta only ever gives us the customer's WhatsApp id
/// (wa_id) — never our internal ContactId/ConversationId — so this resolves (or creates) both first,
/// then hands off to IMessageService.IngestAsync exactly as the earlier normalized ingest endpoint
/// did. No persistence/SignalR logic is duplicated here; this class is purely the "what do I need
/// to look up before I can call the existing service" adapter.
/// </summary>
public class CustomerMessageHandler
{
    private readonly IContactService _contacts;
    private readonly IConversationService _conversations;
    private readonly IMessageService _messages;

    public CustomerMessageHandler(IContactService contacts, IConversationService conversations, IMessageService messages)
    {
        _contacts = contacts;
        _conversations = conversations;
        _messages = messages;
    }

    public async Task<WhatsAppWebhookResponse> HandleAsync(ParsedMetaEvent evt, ResolvedClient client, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(evt.CustomerWaId))
        {
            return new WhatsAppWebhookResponse(true, "customer_message", false, client.ClientId, client.ChannelIntegrationId);
        }

        // Meta's wa_id is digits only (no leading "+") — normalize to this app's E.164-ish
        // convention ("+<digits>") used elsewhere in Contact.Phone.
        var phone = "+" + evt.CustomerWaId.TrimStart('+');
        var (contact, contactWasCreated) = await _contacts.GetOrCreateByPhoneAsync(client.ClientId, phone, evt.CustomerName, ct);
        var conversation = await _conversations.GetOrCreateForContactAsync(client.ClientId, contact.Id, ConversationChannel.WhatsApp, ct);

        var result = await _messages.IngestAsync(new IngestMessageRequest(
            client.ClientId, conversation.Id, contact.Id, IngestEventType.CustomerMessage, ConversationChannel.WhatsApp,
            evt.Content, evt.ExternalMessageId, DeliveryStatus: null, SentAt: null, ReceivedAt: evt.Timestamp,
            MessageType: evt.MessageType, MetadataJson: evt.MetadataJson, ContactWasNewlyCreated: contactWasCreated), ct);

        return new WhatsAppWebhookResponse(
            Processed: true, EventType: "customer_message", ShouldRunAi: result.AiEligible,
            ClientId: client.ClientId, WhatsAppConnectionId: client.ChannelIntegrationId,
            ConversationId: conversation.Id, ContactId: contact.Id, MessageId: result.Message?.Id,
            ExternalMessageId: evt.ExternalMessageId, Mode: result.ConversationMode,
            MessageType: result.Message?.MessageType, Content: result.Message?.Content,
            SelectedValue: evt.SelectedValue);
    }
}
