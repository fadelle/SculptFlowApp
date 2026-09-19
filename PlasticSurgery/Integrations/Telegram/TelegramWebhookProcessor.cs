using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Integrations.Telegram;

/// <summary>What the webhook controller needs back to decide whether to call n8n.</summary>
public record TelegramWebhookResult(
    bool Processed,
    string EventType,
    bool ShouldRunAi,
    Guid? ClinicId = null,
    Guid? ConversationId = null,
    Guid? LeadId = null,
    Guid? MessageId = null,
    string? MessageType = null,
    string? Content = null,
    string? Mode = null,
    bool Deduplicated = false);

/// <summary>
/// Telegram Update -> normalized SculptFlow records. The Telegram counterpart of MetaWebhookProcessor +
/// CustomerMessageHandler, and just as thin: parse, resolve the lead/conversation, then hand to the
/// existing IMessageService.IngestAsync — which already persists, updates the conversation, broadcasts
/// SignalR and decides AI eligibility. No persistence or SignalR logic is duplicated here.
///
/// The clinic comes from the already-authenticated <see cref="ChannelIntegration"/> (resolved by the
/// controller from the connectionId in the URL and verified against the webhook secret) — never from
/// anything in the Telegram payload.
/// </summary>
public interface ITelegramWebhookProcessor
{
    Task<TelegramWebhookResult> ProcessAsync(ChannelIntegration connection, JsonElement update, CancellationToken ct = default);
}

public class TelegramWebhookProcessor : ITelegramWebhookProcessor
{
    private readonly ApplicationDbContext _db;
    private readonly ILeadService _leads;
    private readonly IConversationService _conversations;
    private readonly IMessageService _messages;
    private readonly ILogger<TelegramWebhookProcessor> _logger;

    public TelegramWebhookProcessor(
        ApplicationDbContext db, ILeadService leads, IConversationService conversations, IMessageService messages,
        ILogger<TelegramWebhookProcessor> logger)
    {
        _db = db;
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
        await _db.ChannelIntegrations.Where(c => c.Id == connectionId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastWebhookAt, now), ct);

        if (parsed.Kind != TelegramUpdateKind.CustomerMessage || parsed.Message is null)
        {
            _logger.LogInformation("Ignoring Telegram update {UpdateId} for connection {ConnectionId}: {Reason}",
                parsed.UpdateId, connectionId, parsed.IgnoredReason);
            return new TelegramWebhookResult(false, "ignored", false, clinicId);
        }

        var msg = parsed.Message;

        // Telegram has no phone number for us: the person is identified by their Telegram chat id,
        // scoped to this clinic (the unique index is (clinic_id, external_lead_id)).
        var lead = await _leads.GetOrCreateByExternalIdAsync(
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
                MessageType: msg.MessageType, MetadataJson: msg.MetadataJson), ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
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
}
