using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public class MessageService : IMessageService
{
    private readonly ApplicationDbContext _db;
    private readonly IWhatsAppService _whatsApp;
    private readonly IEnumerable<IChannelSender> _senders;
    private readonly IInboxNotifier _notifier;
    private readonly IEventLogger _events;

    public MessageService(
        ApplicationDbContext db, IWhatsAppService whatsApp, IEnumerable<IChannelSender> senders,
        IInboxNotifier notifier, IEventLogger events)
    {
        _db = db;
        _whatsApp = whatsApp; // still used directly for WhatsApp-only template sends
        _senders = senders;
        _notifier = notifier;
        _events = events;
    }

    /// <summary>Picks the outbound adapter for a conversation's channel (WhatsApp, Telegram, ...).</summary>
    private IChannelSender ResolveSender(string channel) =>
        _senders.FirstOrDefault(s => s.Channel == channel)
        ?? throw new InvalidOperationException(
            $"Sending is only implemented for WhatsApp and Telegram conversations right now (this one is '{channel}').");

    public async Task<MessageResponse?> SendAsync(Guid clinicId, Guid conversationId, SendMessageRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            throw new ArgumentException("Content is required.", nameof(request));
        }

        var conversation = await _db.Conversations
            .Include(c => c.Lead)
            .FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.Id == conversationId, ct);
        if (conversation is null) return null;

        // Channel-specific rules (WhatsApp's 24h window + phone, Telegram's connected bot + chat id)
        // live in the IChannelSender for this conversation's channel. The actual API call happens
        // before we touch the database — if it throws, no message row or conversation-state change
        // is left behind for a send that never went out.
        var externalMessageId = await ResolveSender(conversation.Channel).SendTextAsync(conversation, request.Content, ct);

        var now = DateTimeOffset.UtcNow;
        var message = new Message
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            ConversationId = conversationId,
            LeadId = conversation.LeadId,
            Direction = MessageDirection.Outbound,
            SenderType = MessageSenderType.Staff,
            Origin = MessageOrigin.Dashboard,
            Channel = conversation.Channel,
            MessageType = string.IsNullOrWhiteSpace(request.MessageType) ? "text" : request.MessageType,
            Content = request.Content,
            ExternalMessageId = externalMessageId,
            IsAiGenerated = false,
            SentAt = now,
            CreatedAt = now
        };
        _db.Messages.Add(message);

        conversation.LastMessageAt = now;
        conversation.LastMessageDirection = MessageDirection.Outbound;
        conversation.UpdatedAt = now;

        // Staff sending from the dashboard means a human is handling this conversation now.
        var modeChanged = conversation.Mode != ConversationMode.Human;
        ConversationModeSync.Apply(conversation, ConversationMode.Human, now);

        if (conversation.Lead is not null)
        {
            conversation.Lead.LastContactAt = now;
            conversation.Lead.UpdatedAt = now;
        }

        // Don't log message content into event metadata — just that a send happened and by whom.
        _events.Log(clinicId, EventTypes.StaffMessageSentDashboard,
            leadId: conversation.LeadId, conversationId: conversationId, source: MessageOrigin.Dashboard);

        await _db.SaveChangesAsync(ct);

        await _notifier.NewMessageAsync(clinicId, conversationId, message.Id, conversation.LeadId,
            MessageDirection.Outbound, MessageSenderType.Staff, MessageOrigin.Dashboard, now, ct);
        if (modeChanged)
        {
            await _notifier.ConversationModeChangedAsync(clinicId, conversationId, ConversationMode.Human, ct);
        }

        return ConversationService.ToResponse(message);
    }

    public async Task<MessageResponse?> SendAiReplyAsync(Guid clinicId, Guid conversationId, string content, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Content is required.", nameof(content));
        }

        var conversation = await _db.Conversations
            .Include(c => c.Lead)
            .FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.Id == conversationId, ct);
        if (conversation is null) return null;

        // The mandatory re-check: a human may have taken over between the webhook that triggered
        // this AI reply and the AI actually finishing its response. Read fresh from the DB — never
        // trust the "mode":"ai" the original webhook response carried.
        if (conversation.Mode != ConversationMode.Ai)
        {
            throw new ConversationNotInAiModeException(conversation.Mode);
        }

        var externalMessageId = await ResolveSender(conversation.Channel).SendTextAsync(conversation, content, ct);

        var now = DateTimeOffset.UtcNow;
        var message = new Message
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            ConversationId = conversationId,
            LeadId = conversation.LeadId,
            Direction = MessageDirection.Outbound,
            SenderType = MessageSenderType.Ai,
            Origin = MessageOrigin.Ai,
            Channel = conversation.Channel,
            MessageType = "text",
            Content = content,
            ExternalMessageId = externalMessageId,
            IsAiGenerated = true,
            SentAt = now,
            CreatedAt = now
        };
        _db.Messages.Add(message);

        // AI sending a reply keeps the conversation in AI mode — never flip to human here (that's
        // what distinguishes this from SendAsync's staff path).
        conversation.LastMessageAt = now;
        conversation.LastMessageDirection = MessageDirection.Outbound;
        conversation.UpdatedAt = now;

        if (conversation.Lead is not null)
        {
            conversation.Lead.LastContactAt = now;
            conversation.Lead.UpdatedAt = now;
        }

        _events.Log(clinicId, EventTypes.AiMessageSent,
            leadId: conversation.LeadId, conversationId: conversationId, source: MessageOrigin.Ai);

        await _db.SaveChangesAsync(ct);

        await _notifier.NewMessageAsync(clinicId, conversationId, message.Id, conversation.LeadId,
            MessageDirection.Outbound, MessageSenderType.Ai, MessageOrigin.Ai, now, ct);

        return ConversationService.ToResponse(message);
    }

    public Task<MessageResponse?> SendTemplateAsync(Guid clinicId, Guid conversationId, SendTemplateMessageRequest request, CancellationToken ct = default) =>
        SendTemplateCoreAsync(clinicId, conversationId, request.WhatsAppTemplateId, request.BodyParameters ?? Array.Empty<string>(),
            MessageOrigin.Dashboard, campaignId: null, campaignRecipientId: null, ct);

    /// <summary>Campaign-origin variant of SendTemplateAsync — same WhatsApp send + Message-row logic,
    /// but stamps Origin.Campaign and links CampaignId/CampaignRecipientId instead of acting as a
    /// plain staff Inbox send. Used exclusively by CampaignService.ProcessRecipientAsync.</summary>
    public Task<MessageResponse?> SendCampaignTemplateAsync(
        Guid clinicId, Guid conversationId, Guid whatsAppTemplateId, IReadOnlyList<string> bodyParameters,
        Guid campaignId, Guid campaignRecipientId, CancellationToken ct = default) =>
        SendTemplateCoreAsync(clinicId, conversationId, whatsAppTemplateId, bodyParameters,
            MessageOrigin.Campaign, campaignId, campaignRecipientId, ct);

    private async Task<MessageResponse?> SendTemplateCoreAsync(
        Guid clinicId, Guid conversationId, Guid whatsAppTemplateId, IReadOnlyList<string> bodyParameters,
        string origin, Guid? campaignId, Guid? campaignRecipientId, CancellationToken ct)
    {
        var conversation = await _db.Conversations
            .Include(c => c.Lead)
            .FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.Id == conversationId, ct);
        if (conversation is null) return null;

        if (conversation.Channel != ConversationChannel.WhatsApp)
        {
            throw new InvalidOperationException(
                $"Templates are WhatsApp-only — this conversation is on '{conversation.Channel}'.");
        }

        var template =await _db.WhatsAppTemplates.FirstOrDefaultAsync(
            t => t.ClinicId == clinicId && t.Id == whatsAppTemplateId, ct);
        if (template is null)
        {
            throw new ArgumentException("Template not found for this clinic.", nameof(whatsAppTemplateId));
        }
        if (template.Status != WhatsAppTemplateStatus.Approved)
        {
            throw new InvalidOperationException(
                $"Template '{template.Name}' isn't approved yet (status: {template.Status}) — Meta only allows sending approved templates.");
        }

        var toPhone = conversation.Lead?.Phone
            ?? throw new InvalidOperationException("This lead has no phone number on file — can't send a WhatsApp message.");

        var externalMessageId = await _whatsApp.SendTemplateMessageAsync(
            clinicId, toPhone, template.Name, template.Language, bodyParameters, ct);

        var now = DateTimeOffset.UtcNow;
        var message = new Message
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            ConversationId = conversationId,
            LeadId = conversation.LeadId,
            Direction = MessageDirection.Outbound,
            SenderType = MessageSenderType.Staff,
            Origin = origin,
            Channel = ConversationChannel.WhatsApp,
            MessageType = "template",
            Content = RenderTemplatePreview(template.Body, bodyParameters),
            ExternalMessageId = externalMessageId,
            WhatsAppTemplateId = template.Id,
            CampaignId = campaignId,
            CampaignRecipientId = campaignRecipientId,
            IsAiGenerated = false,
            SentAt = now,
            CreatedAt = now
        };
        _db.Messages.Add(message);

        // A template send (Inbox or Campaign) is still the business proactively reaching out — not
        // customer-initiated, so it must NOT touch LastCustomerMessageAt/ServiceWindowExpiresAt.
        conversation.LastMessageAt = now;
        conversation.LastMessageDirection = MessageDirection.Outbound;
        conversation.UpdatedAt = now;

        var modeChanged = conversation.Mode != ConversationMode.Human;
        ConversationModeSync.Apply(conversation, ConversationMode.Human, now);

        if (conversation.Lead is not null)
        {
            conversation.Lead.LastContactAt = now;
            conversation.Lead.UpdatedAt = now;
        }

        var eventType = origin == MessageOrigin.Campaign ? EventTypes.CampaignMessageSent : EventTypes.StaffMessageSentDashboard;
        _events.Log(clinicId, eventType, leadId: conversation.LeadId, conversationId: conversationId, source: origin);

        await _db.SaveChangesAsync(ct);

        await _notifier.NewMessageAsync(clinicId, conversationId, message.Id, conversation.LeadId,
            MessageDirection.Outbound, MessageSenderType.Staff, origin, now, ct);
        if (modeChanged)
        {
            await _notifier.ConversationModeChangedAsync(clinicId, conversationId, ConversationMode.Human, ct);
        }

        return ConversationService.ToResponse(message);
    }

    /// <summary>Substitutes {{1}}, {{2}}... in a template body with the given values, purely for a
    /// human-readable Message.Content — Meta gets the real positional parameters separately.</summary>
    internal static string RenderTemplatePreview(string body, IReadOnlyList<string> parameters)
    {
        var rendered = body;
        for (var i = 0; i < parameters.Count; i++)
        {
            rendered = rendered.Replace("{{" + (i + 1) + "}}", parameters[i]);
        }
        return rendered;
    }

    public async Task<IngestMessageResult> IngestAsync(IngestMessageRequest request, CancellationToken ct = default)
    {
        if (!IngestEventType.All.Contains(request.EventType))
        {
            throw new ArgumentException($"Invalid eventType '{request.EventType}'.", nameof(request));
        }

        if (request.EventType == IngestEventType.StatusUpdate)
        {
            return await HandleStatusUpdateAsync(request, ct);
        }

        var conversation = await _db.Conversations
            .FirstOrDefaultAsync(c => c.ClinicId == request.ClinicId && c.Id == request.ConversationId, ct);
        if (conversation is null)
        {
            return new IngestMessageResult(Found: false, Deduplicated: false, null);
        }

        // Idempotency: Meta/n8n webhook deliveries can retry. If we've already recorded this exact
        // external message for this clinic+channel, hand back the existing row instead of a duplicate.
        if (!string.IsNullOrWhiteSpace(request.ExternalMessageId))
        {
            var existing = await _db.Messages.FirstOrDefaultAsync(m =>
                m.ClinicId == request.ClinicId && m.Channel == request.Channel && m.ExternalMessageId == request.ExternalMessageId, ct);
            if (existing is not null)
            {
                return new IngestMessageResult(Found: true, Deduplicated: true, ConversationService.ToResponse(existing),
                    conversation.Mode, AiEligible: false);
            }
        }

        var (direction, senderType, origin, isAiGenerated, moveToHuman, eventType) = request.EventType switch
        {
            IngestEventType.CustomerMessage => (
                MessageDirection.Inbound, MessageSenderType.Lead, MessageOrigin.CustomerFor(request.Channel),
                false, false, EventTypes.CustomerMessageReceived),

            // Coexistence echo: staff replied from the WhatsApp Business phone app directly. This
            // is never a customer message and must never trigger the AI — force human mode.
            IngestEventType.BusinessAppEcho => (
                MessageDirection.Outbound, MessageSenderType.Staff, MessageOrigin.WhatsAppBusinessApp,
                false, true, EventTypes.StaffMessageSentWhatsAppApp),

            IngestEventType.AiMessage => (
                MessageDirection.Outbound, MessageSenderType.Ai, MessageOrigin.Ai,
                true, false, EventTypes.AiMessageSent),

            _ => throw new ArgumentException($"Unsupported eventType '{request.EventType}' for message creation.", nameof(request))
        };

        var now = DateTimeOffset.UtcNow;
        var message = new Message
        {
            Id = Guid.NewGuid(),
            ClinicId = request.ClinicId,
            ConversationId = request.ConversationId,
            LeadId = request.LeadId,
            Direction = direction,
            SenderType = senderType,
            Origin = origin,
            Channel = request.Channel,
            MessageType = string.IsNullOrWhiteSpace(request.MessageType) ? "text" : request.MessageType,
            Content = request.Content,
            ExternalMessageId = request.ExternalMessageId,
            DeliveryStatus = request.DeliveryStatus,
            MetadataJson = request.MetadataJson,
            IsAiGenerated = isAiGenerated,
            SentAt = direction == MessageDirection.Outbound ? request.SentAt ?? now : null,
            ReceivedAt = direction == MessageDirection.Inbound ? request.ReceivedAt ?? now : null,
            CreatedAt = now
        };
        _db.Messages.Add(message);

        conversation.LastMessageAt = now;
        conversation.LastMessageDirection = direction;
        conversation.UpdatedAt = now;

        // The 24h WhatsApp service window only ever opens/extends on a genuine customer message —
        // business_app_echo, ai_message (and elsewhere: dashboard, campaign sends) must NOT touch
        // this, or staff/AI could keep a window open indefinitely just by replying to themselves.
        if (request.EventType == IngestEventType.CustomerMessage)
        {
            conversation.LastCustomerMessageAt = now;
            // The 24h window is a WhatsApp rule only — other channels (Telegram) have no such limit.
            if (conversation.Channel == ConversationChannel.WhatsApp)
            {
                conversation.ServiceWindowExpiresAt = now.AddHours(24);
            }
        }

        var modeChanged = false;
        if (moveToHuman && conversation.Mode != ConversationMode.Human)
        {
            ConversationModeSync.Apply(conversation, ConversationMode.Human, now);
            modeChanged = true;
        }

        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == request.LeadId, ct);
        if (lead is not null)
        {
            lead.LastContactAt = now;
            lead.UpdatedAt = now;
        }

        _events.Log(request.ClinicId, eventType,
            leadId: request.LeadId, conversationId: request.ConversationId, source: origin);

        await _db.SaveChangesAsync(ct);

        await _notifier.NewMessageAsync(request.ClinicId, request.ConversationId, message.Id, request.LeadId,
            direction, senderType, origin, now, ct);
        if (modeChanged)
        {
            await _notifier.ConversationModeChangedAsync(request.ClinicId, request.ConversationId, ConversationMode.Human, ct);
        }

        // The strict AI-trigger rule (see IngestMessageResult's doc comment): only a genuine
        // customer message, in a conversation currently in AI mode, with a message type the AI is
        // equipped to handle. Media/location/contact/reaction messages are saved but never reach
        // the AI "initially" per the event-routing table — extend AiEligibleMessageTypes if/when
        // the AI agent gains multimodal handling.
        var aiEligible = request.EventType == IngestEventType.CustomerMessage
            && conversation.Mode == ConversationMode.Ai
            && AiEligibleMessageTypes.Contains(message.MessageType);

        return new IngestMessageResult(Found: true, Deduplicated: false, ConversationService.ToResponse(message),
            conversation.Mode, aiEligible);
    }

    /// <summary>Customer message types the AI agent is allowed to see — see IngestMessageResult.AiEligible.</summary>
    private static readonly HashSet<string> AiEligibleMessageTypes = new(StringComparer.OrdinalIgnoreCase) { "text", "interactive" };
    // (Telegram commands like /start arrive as message type "command" and media as "image"/"audio"/…,
    // so they're saved to the Inbox but never reach the AI.)

    private async Task<IngestMessageResult> HandleStatusUpdateAsync(IngestMessageRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ExternalMessageId))
        {
            throw new ArgumentException("ExternalMessageId is required for a status_update.", nameof(request));
        }

        var message = await _db.Messages.FirstOrDefaultAsync(m =>
            m.ClinicId == request.ClinicId && m.Channel == request.Channel && m.ExternalMessageId == request.ExternalMessageId, ct);
        if (message is null)
        {
            return new IngestMessageResult(Found: false, Deduplicated: false, null);
        }

        // Not a new message — just update delivery status and tell the Inbox. Never touches AI/mode.
        var occurredAt = request.OccurredAt ?? DateTimeOffset.UtcNow;
        message.DeliveryStatus = request.DeliveryStatus;
        switch (request.DeliveryStatus?.ToLowerInvariant())
        {
            case "sent":
                message.SentAt ??= occurredAt;
                break;
            case "delivered":
                message.DeliveredAt = occurredAt;
                break;
            case "read":
                message.ReadAt = occurredAt;
                break;
            case "failed":
                message.FailedAt = occurredAt;
                message.FailureCode = request.FailureCode;
                message.FailureReason = request.FailureReason;
                break;
            case "deleted":
                message.DeletedAt = occurredAt;
                break;
        }

        // If this message belongs to a Campaign, roll the same status into its CampaignRecipient
        // so campaign stats (CampaignStatsResponse) stay in sync — still just one Message row, no
        // second write path.
        if (message.CampaignRecipientId.HasValue)
        {
            var recipient = await _db.CampaignRecipients.FirstOrDefaultAsync(r => r.Id == message.CampaignRecipientId.Value, ct);
            if (recipient is not null)
            {
                ApplyDeliveryStatusToRecipient(recipient, request.DeliveryStatus, request.FailureCode, request.FailureReason);
            }
        }

        await _db.SaveChangesAsync(ct);

        await _notifier.MessageStatusUpdatedAsync(
            request.ClinicId, message.ConversationId, message.Id, request.DeliveryStatus ?? "unknown", occurredAt, ct);

        // Status updates are explicitly excluded from the AI trigger — never eligible.
        return new IngestMessageResult(Found: true, Deduplicated: false, ConversationService.ToResponse(message), AiEligible: false);
    }

    private static void ApplyDeliveryStatusToRecipient(CampaignRecipient recipient, string? deliveryStatus, string? failureCode, string? failureReason)
    {
        var now = DateTimeOffset.UtcNow;
        switch (deliveryStatus?.ToLowerInvariant())
        {
            case "delivered":
                recipient.Status = CampaignRecipientStatus.Delivered;
                recipient.DeliveredAt = now;
                break;
            case "read":
                recipient.Status = CampaignRecipientStatus.Read;
                recipient.ReadAt = now;
                break;
            case "failed":
                recipient.Status = CampaignRecipientStatus.Failed;
                recipient.FailedAt = now;
                recipient.FailureCode = failureCode;
                recipient.FailureReason = failureReason;
                break;
            case "sent":
                // Already Sent from ProcessRecipientAsync — nothing new to record.
                break;
        }
        recipient.UpdatedAt = now;
    }
}
