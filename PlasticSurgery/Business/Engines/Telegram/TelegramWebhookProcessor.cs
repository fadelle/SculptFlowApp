using System.Text.Json;
using PlasticSurgery.Business.Contracts.Engines.Telegram;
using PlasticSurgery.Business.Contracts.HttpClients.N8n;
using PlasticSurgery.Business.Contracts.Services.Inbox;
using PlasticSurgery.Business.Contracts.Services.Leads;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Exceptions;
using PlasticSurgery.Common.Helpers;
using PlasticSurgery.Entities.Dtos.Ai;
using PlasticSurgery.Entities.Dtos.Inbox;
using PlasticSurgery.Entities.Dtos.Telegram;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Requests.Inbox;
using PlasticSurgery.Persistence.Contracts.Channels;

namespace PlasticSurgery.Business.Engines.Telegram;

public class TelegramWebhookProcessor : ITelegramWebhookProcessor
{
    private readonly IChannelIntegrationRepository _integrations;
    private readonly IAiTriggerNotifier _aiTrigger;
    private readonly ILeadService _leads;
    private readonly IConversationService _conversations;
    private readonly IMessageService _messages;
    private readonly ILogger<TelegramWebhookProcessor> _logger;

    public TelegramWebhookProcessor(
        IChannelIntegrationRepository integrations, IAiTriggerNotifier aiTrigger, ILeadService leads, IConversationService conversations, IMessageService messages,
        ILogger<TelegramWebhookProcessor> logger)
    {
        _integrations = integrations;
        _aiTrigger = aiTrigger;
        _leads = leads;
        _conversations = conversations;
        _messages = messages;
        _logger = logger;
    }

    public async Task<TelegramWebhookResult> ProcessAsync(ChannelIntegration connection, JsonElement update, CancellationToken ct = default)
    {
        var clinicId = connection.ClinicId;
        var connectionId = connection.Id;

        var parsed = TelegramUpdateParser.Parse(update);

        // Cheap liveness marker for the Integrations page ("last update received"); doesn't go through
        // the change tracker so it can't interfere with anything the ingest below saves.
        var now = DateTimeOffset.UtcNow;
        await _integrations.TouchLastWebhookAsync(connectionId, now, ct);

        if (parsed.Kind != TelegramUpdateKind.CustomerMessage || parsed.Message is null)
        {
            _logger.LogInformation("Ignoring Telegram update {UpdateId} for connection {ConnectionId}: {Reason}",
                parsed.UpdateId, connectionId, parsed.IgnoredReason);
            return new TelegramWebhookResult(false, "ignored", false, clinicId);
        }

        var msg = parsed.Message;

        // Telegram has no phone number for us: the person is identified by their Telegram chat id,
        // scoped to this clinic (the unique index is (clinic_id, external_lead_id)).
        var (lead, leadWasCreated) = await _leads.GetOrCreateByExternalIdAsync(
            clinicId,
            externalLeadId: $"telegram:{msg.ChatId}",
            source: ConversationChannel.Telegram,
            fullName: DisplayName(msg),
            firstName: msg.FirstName,
            lastName: msg.LastName,
            sourceDetail: string.IsNullOrEmpty(msg.Username) ? null : "@" + msg.Username,
            ct);

        var conversation = await _conversations.GetOrCreateForLeadAsync(
            clinicId, lead.Id, ConversationChannel.Telegram, msg.ChatId.ToString(), ct);

        IngestMessageResult result;
        try
        {
            result = await _messages.IngestAsync(new IngestMessageRequest(
                clinicId, conversation.Id, lead.Id, IngestEventType.CustomerMessage, ConversationChannel.Telegram,
                msg.Content,
                // Telegram message_ids are only unique within a chat, so the chat id is part of the key;
                // (clinic_id, channel, external_message_id) is the DB-level idempotency guarantee.
                ExternalMessageId: ExternalMessageId(msg.ChatId.ToString(), msg.MessageId),
                DeliveryStatus: null, SentAt: null, ReceivedAt: msg.Timestamp,
                MessageType: msg.MessageType, MetadataJson: msg.MetadataJson, LeadWasNewlyCreated: leadWasCreated), ct);
        }
        catch (DuplicateRecordException)
        {
            // Two deliveries of the same update raced past the "already ingested?" check; the unique
            // index kept exactly one row. The other delivery owns the AI trigger, so this one must not.
            _logger.LogInformation("Duplicate Telegram message {ChatId}:{MessageId} lost the insert race.", msg.ChatId, msg.MessageId);
            return new TelegramWebhookResult(true, "customer_message", false, clinicId, conversation.Id, lead.Id, Deduplicated: true);
        }

        return new TelegramWebhookResult(
            Processed: true,
            EventType: "customer_message",
            ShouldRunAi: result.AiEligible,
            ClinicId: clinicId,
            ConversationId: conversation.Id,
            LeadId: lead.Id,
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
            return WebhookReceiveOutcome.NotFound;
        }

        if (!TelegramWebhookSecret.Matches(connection.WebhookVerifyToken, secret))
        {
            _logger.LogWarning("Telegram webhook secret mismatch for connection {ConnectionId}.", connectionId);
            return WebhookReceiveOutcome.Forbidden;
        }

        var result = await ProcessAsync(connection, update, ct);

        if (result.ShouldRunAi && result.ClinicId.HasValue && result.ConversationId.HasValue)
        {
            // Same normalized trigger WhatsApp sends — only Channel differs. Never raw Telegram JSON.
            await _aiTrigger.NotifyAsync(new AiTriggerPayload(
                result.ClinicId.Value, result.ConversationId.Value, result.LeadId, result.MessageId,
                Channel: ConversationChannel.Telegram, MessageType: result.MessageType, MessageText: result.Content,
                SelectedValue: null), ct);
        }

        return WebhookReceiveOutcome.Accepted;
    }
}
