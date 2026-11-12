using Omni.App.Common.Statics;
using System.Text.Json;
using Omni.App.Business.Contracts.Engines.Telegram;
using Omni.App.Business.Contracts.Jobs;
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
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Channels;
using Omni.App.Persistence.Contracts.Contacts;
using Omni.App.Persistence.Contracts.Inbox;

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
    private readonly IMessageRepository? _messageRows;
    private readonly IContactRepository? _contactRows;
    private readonly IOptOutService? _optOut;
    private readonly IUnitOfWork? _unitOfWork;
    private readonly IAiTriggerDispatchQueue? _deferredTriggers;

    public TelegramWebhookProcessor(
        IChannelIntegrationRepository integrations, IAiTriggerNotifier aiTrigger, IContactService contacts, IConversationService conversations, IMessageService messages,
        ILogger<TelegramWebhookProcessor> logger, IMessageTracer tracer, IMessageRepository? messageRows = null, IContactRepository? contactRows = null,
        IOptOutService? optOut = null, IUnitOfWork? unitOfWork = null, IAiTriggerDispatchQueue? deferredTriggers = null)
    {
        _deferredTriggers = deferredTriggers;
        _messageRows = messageRows;
        _contactRows = contactRows;
        _optOut = optOut;
        _unitOfWork = unitOfWork;
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
        if (parsed.Message is not null && parsed.Kind is TelegramUpdateKind.EditedMessage or TelegramUpdateKind.BotBlocked or TelegramUpdateKind.BotUnblocked)
        {
            var handled = parsed.Kind == TelegramUpdateKind.EditedMessage
                ? await ApplyEditAsync(clientId, parsed.Message, ct)
                : await ApplyBlockAsync(clientId, parsed.Message, blocked: parsed.Kind == TelegramUpdateKind.BotBlocked, ct);
            _tracer.Step(handled ? TraceSteps.WebhookReceived : TraceSteps.WebhookIgnored, handled ? TraceOutcomes.Ok : TraceOutcomes.Skipped, channelIds,
                detail: new { type = parsed.Kind.ToString(), handled });
            return new TelegramWebhookResult(handled, parsed.Kind == TelegramUpdateKind.EditedMessage ? "edited_message" : "member_update", false, clientId);
        }
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
            Deduplicated: result.Deduplicated,
            SentAt: msg.Timestamp,
            ChatMessageId: msg.MessageId);
    }

    /// <summary>An edit of a message Omni has (QA t3-inbox-17): its text becomes the new one, and its metadata says when it was
    /// edited (the Inbox shows "edited"). An edit of a message Omni never stored is ignored. Never triggers the AI again.</summary>
    private async Task<bool> ApplyEditAsync(Guid clientId, ParsedTelegramMessage edit, CancellationToken ct)
    {
        if (_messageRows is null || _unitOfWork is null) return false;
        var stored = await _messageRows.FindByExternalIdAsync(clientId, ConversationChannel.Telegram, ExternalMessageId(edit.ChatId.ToString(), edit.MessageId), ct);
        if (stored is null || stored.Direction != MessageDirection.Inbound) return false;
        stored.Content = edit.Content;
        stored.MetadataJson = WithEditedAt(stored.MetadataJson, edit.Timestamp);
        await _unitOfWork.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>The message's metadata with "editedAt": a JSON object gets the property; none becomes {"editedAt": …}; a media
    /// array (a photo's sizes) is kept as it is, since the media reader needs it.</summary>
    public static string? WithEditedAt(string? metadata, DateTimeOffset at)
    {
        var node = string.IsNullOrWhiteSpace(metadata) ? new System.Text.Json.Nodes.JsonObject() : System.Text.Json.Nodes.JsonNode.Parse(metadata);
        if (node is not System.Text.Json.Nodes.JsonObject obj) return metadata;
        obj["editedAt"] = at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        return obj.ToJsonString();
    }

    /// <summary>Blocking the bot opts the contact out (campaigns, reminders and the AI stop; sends would fail anyway), starting
    /// it again opts them back in: both are the person's own explicit act (QA t3-inbox-17).</summary>
    private async Task<bool> ApplyBlockAsync(Guid clientId, ParsedTelegramMessage member, bool blocked, CancellationToken ct)
    {
        if (_contactRows is null || _optOut is null || _unitOfWork is null) return false;
        var contact = await _contactRows.FindByExternalIdAsync(clientId, $"telegram:{member.ChatId}", ct);
        if (contact is null) return false;
        if (!_optOut.Apply(contact, blocked, blocked ? "telegram_blocked" : "telegram_unblocked")) return false;
        await _unitOfWork.SaveChangesAsync(ct);
        return true;
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
            var trigger = new AiTriggerPayload(
                result.ClientId.Value, result.ConversationId.Value, result.ContactId, result.MessageId,
                Channel: ConversationChannel.Telegram, MessageType: result.MessageType, MessageText: result.Content,
                SelectedValue: null, SentAt: result.SentAt, ProviderOrder: result.ChatMessageId) { Traceparent = TraceContext.CurrentTraceparent() };
            // The message is saved: answer Telegram now and let the plan/bot checks and the n8n call run afterwards (QA t3-inbox-22).
            // Without the queue (tests) or when it is full, trigger inline as before.
            if (_deferredTriggers?.TryEnqueue(trigger) != true) await _aiTrigger.NotifyAsync(trigger, ct);
        }

        return WebhookReceiveOutcome.Accepted;
    }
}
