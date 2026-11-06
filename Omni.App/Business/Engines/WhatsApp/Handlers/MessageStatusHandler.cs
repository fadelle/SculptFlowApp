using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Monitoring;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.WhatsApp;
using Omni.App.Entities.Requests.Inbox;
using Omni.App.Entities.Responses.WhatsApp;

namespace Omni.App.Business.Engines.WhatsApp.Handlers;

/// <summary>Handles ParsedMetaEventKind.MessageStatus (sent/delivered/read/failed/deleted) — finds
/// the existing Message by external_message_id via IMessageService.IngestAsync's status_update
/// path; never creates a new row. ConversationId/ContactId aren't known (and aren't needed — the
/// existing message is looked up by external id), so Guid.Empty is passed for those unused
/// IngestMessageRequest fields.</summary>
public class MessageStatusHandler
{
    private readonly IMessageService _messages;
    private readonly IMessageTracer? _tracer;

    public MessageStatusHandler(IMessageService messages, IMessageTracer? tracer = null)
    {
        _messages = messages;
        _tracer = tracer;
    }

    public async Task<WhatsAppWebhookResponse> HandleAsync(ParsedMetaEvent evt, ResolvedClient client, CancellationToken ct)
    {
        // A status Omni doesn't know (Meta adds some now and then) is left alone rather than refused, so the webhook still succeeds.
        if (string.IsNullOrWhiteSpace(evt.DeliveryStatus) || !MessageDeliveryStatus.All.Contains(evt.DeliveryStatus))
        {
            _tracer?.Step(TraceSteps.WebhookIgnored, TraceOutcomes.Skipped,
                new TraceIds(client.ClientId, Channel: ConversationChannel.WhatsApp, ExternalMessageId: evt.ExternalMessageId),
                detail: new { reason = "status_unknown", status = evt.DeliveryStatus });
            return new WhatsAppWebhookResponse(
                Processed: false, EventType: "message_status", ShouldRunAi: false,
                ClientId: client.ClientId, WhatsAppConnectionId: client.ChannelIntegrationId,
                ConversationId: null, ContactId: null, MessageId: null,
                ExternalMessageId: evt.ExternalMessageId, Status: evt.DeliveryStatus);
        }

        var result = await _messages.IngestAsync(new IngestMessageRequest(
            client.ClientId, Guid.Empty, Guid.Empty, IngestEventType.StatusUpdate, ConversationChannel.WhatsApp,
            Content: null, evt.ExternalMessageId, evt.DeliveryStatus, SentAt: null, ReceivedAt: null,
            MessageType: null, MetadataJson: null, FailureCode: evt.FailureCode, FailureReason: evt.FailureReason,
            OccurredAt: evt.Timestamp), ct);

        // A status for a message Omni never sent leaves a trace, so a missing message can be diagnosed (QA t10-12).
        if (!result.Found)
        {
            _tracer?.Step(TraceSteps.WebhookIgnored, TraceOutcomes.Skipped,
                new TraceIds(client.ClientId, Channel: ConversationChannel.WhatsApp, ExternalMessageId: evt.ExternalMessageId),
                detail: new { reason = "status_unmatched", status = evt.DeliveryStatus });
        }

        return new WhatsAppWebhookResponse(
            Processed: result.Found, EventType: "message_status", ShouldRunAi: false,
            ClientId: client.ClientId, WhatsAppConnectionId: client.ChannelIntegrationId,
            ConversationId: result.Message?.ConversationId, ContactId: result.Message?.ContactId, MessageId: result.Message?.Id,
            ExternalMessageId: evt.ExternalMessageId, Status: evt.DeliveryStatus);
    }
}
