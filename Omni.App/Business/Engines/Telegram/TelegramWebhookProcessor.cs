using Omni.App.Common.Statics;
using System.Text.Json;
using Omni.App.Business.Contracts.Engines.Telegram;
using Omni.App.Business.Contracts.HttpClients.N8n;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Business.Contracts.Services.Contacts;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.Ai;
using Omni.App.Entities.Dtos.Inbox;
using Omni.App.Entities.Dtos.Monitoring;
using Omni.App.Entities.Dtos.Telegram;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Inbox;
using Omni.App.Persistence.Contracts.Channels;

namespace Omni.App.Business.Engines.Telegram;

public class TelegramWebhookProcessor : ITelegramWebhookProcessor
{
    private readonly IChannelIntegrationRepository _integrations;
    private readonly IAiTriggerNotifier _aiTrigger;
    private readonly IContactService _contacts;
    private readonly IConversationService _conversations;
    private readonly IMessageService _messages;
    private readonly ILogger<TelegramWebhookProcessor> _logger;
    private readonly IMessageTracer _tracer;

    public TelegramWebhookProcessor(
        IChannelIntegrationRepository integrations, IAiTriggerNotifier aiTrigger, IContactService contacts, IConversationService conversations, IMessageService messages,
        ILogger<TelegramWebhookProcessor> logger, IMessageTracer tracer)
    {
        _tracer = tracer;
        _integrations = integrations;
        _aiTrigger = aiTrigger;
        _contacts = contacts;
        _conversations = conversations;
        _messages = messages;
        _logger = logger;
    }

    public async Task<TelegramWebhookResult> ProcessAsync(ChannelIntegration connection, JsonElement update, CancellationToken ct = default)
    {
        var clientId = connection.ClientId;
        var connectionId = connection.Id;

        var parsed = TelegramUpdateParser.Parse(update);

        // Cheap liveness marker for the Integrations page ("last update received"); doesn't go through
        // the change tracker so it can't interfere with anything the ingest below saves.
        var now = DateTimeOffset.UtcNow;
        await _integrations.TouchLastWebhookAsync(connectionId, now, ct);

        var channelIds = new TraceIds(clientId, Channel: ConversationChannel.Telegram);
        if (parsed.Kind != TelegramUpdateKind.CustomerMessage || parsed.Message is null)
        {
            _tracer.Step(TraceSteps.WebhookIgnored, TraceOutcomes.Skipped, channelIds, detail: new { reason = parsed.IgnoredReason });
            _logger.LogInformation("Ignoring Telegram update {UpdateId} for connection {ConnectionId}: {Reason}",
                parsed.UpdateId, connectionId, parsed.IgnoredReason);
            return new TelegramWebhookResult(false, "ignored", false, clientId);
        }

        var msg = parsed.Message;
        _tracer.Step(TraceSteps.WebhookReceived, TraceOutcomes.Ok, channelIds, detail: new { type = msg.MessageType });

        // Telegram has no phone number for us: the person is identified by their Telegram chat id,
        // scoped to this client (the unique index is (client_id, external_contact_id)).
        var (contact, contactWasCreated) = await _contacts.GetOrCreateByExternalIdAsync(
            clientId,
            externalContactId: $"telegram:{msg.ChatId}",
            source: ConversationChannel.Telegram,
            fullName: DisplayName(msg),
            firstName: msg.FirstName,
            lastName: msg.LastName,
            sourceDetail: string.IsNullOrEmpty(msg.Username) ? null : "@" + msg.Username,
            ct);

        var conversation = await _conversations.GetOrCreateForContactAsync(
            clientId, contact.Id, ConversationChannel.Telegram, msg.ChatId.ToString(), ct);

        IngestMessageResult result;
        try
        {
            result = await _messages.IngestAsync(new IngestMessageRequest(
                clientId, conversation.Id, contact.Id, IngestEventType.CustomerMessage, ConversationChannel.Telegram,
                msg.Content,
                // Telegram message_ids are only unique within a chat, so the chat id is part of the key;
                // (client_id, channel, external_message_id) is the DB-level idempotency guarantee.
                ExternalMessageId: ExternalMessageId(msg.ChatId.ToString(), msg.MessageId),
                DeliveryStatus: null, SentAt: null, ReceivedAt: msg.Timestamp,
                MessageType: msg.MessageType, MetadataJson: msg.MetadataJson, ContactWasNewlyCreated: contactWasCreated), ct);
        }
        catch (DuplicateRecordException)
        {
            // Two deliveries of the same update raced past the "already ingested?" check; the unique
            // index kept exactly one row. The other delivery owns the AI trigger, so this one must not.
            _logger.LogInformation("Duplicate Telegram message {ChatId}:{MessageId} lost the insert race.", msg.ChatId, msg.MessageId);
            _tracer.Step(TraceSteps.MessageDeduplicated, TraceOutcomes.Skipped,
                channelIds with { ConversationId = conversation.Id, ExternalMessageId = ExternalMessageId(msg.ChatId.ToString(), msg.MessageId) },
                detail: new { reason = "insert_race" });
            return new TelegramWebhookResult(true, "customer_message", false, clientId, conversation.Id, contact.Id, Deduplicated: true);
        }

        return new TelegramWebhookResult(
            Processed: true,
            EventType: "customer_message",
            ShouldRunAi: result.AiEligible,
            ClientId: clientId,
            ConversationId: conversation.Id,
            ContactId: contact.Id,
            MessageId: result.Message?.Id,
            MessageType: result.Message?.MessageType,
            Content: result.Message?.Content,
            Mode: result.ConversationMode,
            Deduplicated: result.Deduplicated);
    }

    /// <summary>The stable external id stored on both inbound and outbound Telegram messages.</summary>
    public static string ExternalMessageId(string chatId, long messageId) => $"{chatId}:{messageId}";

    private static string DisplayName(ParsedTelegramMessage m)
    {
        var name = string.Join(" ", new[] { m.FirstName, m.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (name.Length > 0) return name;
        return string.IsNullOrEmpty(m.Username) ? $"Telegram user {m.ChatId}" : "@" + m.Username;
    }

    public async Task<WebhookReceiveOutcome> ReceiveAsync(Guid connectionId, string? secret, JsonElement update,
        CancellationToken ct = default)
    {
        var connection = await _integrations.GetByIdReadOnlyAsync(connectionId, ct);

        if (connection is null
            || connection.Channel != ChannelType.Telegram
            || connection.Status != ChannelIntegrationStatus.Connected)
        {
            _tracer.Step(TraceSteps.WebhookRejected, TraceOutcomes.Failed, new TraceIds(Channel: ConversationChannel.Telegram),
                detail: new { reason = "unknown_connection" });
            return WebhookReceiveOutcome.NotFound;
        }

        if (!TelegramWebhookSecret.Matches(connection.WebhookVerifyToken, secret))
        {
            _logger.LogWarning("Telegram webhook secret mismatch for connection {ConnectionId}.", connectionId);
            _tracer.Step(TraceSteps.WebhookRejected, TraceOutcomes.Failed, new TraceIds(connection.ClientId, Channel: ConversationChannel.Telegram),
                detail: new { reason = "secret" });
            return WebhookReceiveOutcome.Forbidden;
        }

        var result = await ProcessAsync(connection, update, ct);

        if (result.ShouldRunAi && result.ClientId.HasValue && result.ConversationId.HasValue)
        {
            // Same normalized trigger WhatsApp sends — only Channel differs. Never raw Telegram JSON.
            await _aiTrigger.NotifyAsync(new AiTriggerPayload(
                result.ClientId.Value, result.ConversationId.Value, result.ContactId, result.MessageId,
                Channel: ConversationChannel.Telegram, MessageType: result.MessageType, MessageText: result.Content,
                SelectedValue: null), ct);
        }

        return WebhookReceiveOutcome.Accepted;
    }
}
