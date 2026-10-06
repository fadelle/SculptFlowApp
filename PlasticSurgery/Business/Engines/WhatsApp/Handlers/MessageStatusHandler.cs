using PlasticSurgery.Business.Contracts.Services.Inbox;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.WhatsApp;
using PlasticSurgery.Entities.Requests.Inbox;
using PlasticSurgery.Entities.Responses.WhatsApp;

namespace PlasticSurgery.Business.Engines.WhatsApp.Handlers;

/// <summary>Handles ParsedMetaEventKind.MessageStatus (sent/delivered/read/failed/deleted) — finds
/// the existing Message by external_message_id via IMessageService.IngestAsync's status_update
/// path; never creates a new row. ConversationId/LeadId aren't known (and aren't needed — the
/// existing message is looked up by external id), so Guid.Empty is passed for those unused
/// IngestMessageRequest fields.</summary>
public class MessageStatusHandler
{
    private readonly IMessageService _messages;

    public MessageStatusHandler(IMessageService messages)
    {
        _messages = messages;
    }

    public async Task<WhatsAppWebhookResponse> HandleAsync(ParsedMetaEvent evt, ResolvedClinic clinic, CancellationToken ct)
    {
        var result = await _messages.IngestAsync(new IngestMessageRequest(
            clinic.ClinicId, Guid.Empty, Guid.Empty, IngestEventType.StatusUpdate, ConversationChannel.WhatsApp,
            Content: null, evt.ExternalMessageId, evt.DeliveryStatus, SentAt: null, ReceivedAt: null,
            MessageType: null, MetadataJson: null, FailureCode: evt.FailureCode, FailureReason: evt.FailureReason,
            OccurredAt: evt.Timestamp), ct);

        return new WhatsAppWebhookResponse(
            Processed: result.Found, EventType: "message_status", ShouldRunAi: false,
            ClinicId: clinic.ClinicId, WhatsAppConnectionId: clinic.ChannelIntegrationId,
            ConversationId: result.Message?.ConversationId, LeadId: result.Message?.LeadId, MessageId: result.Message?.Id,
            ExternalMessageId: evt.ExternalMessageId, Status: evt.DeliveryStatus);
    }
}
