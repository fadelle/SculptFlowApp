using System.Diagnostics;
using Omni.App.Business.Contracts.Engines.Billing;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Providers.Channels;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Business.Contracts.Services.Notifications;
using Omni.App.Business.Mappers.Inbox;
using Omni.App.Business.Services.WhatsApp;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Billing;
using Omni.App.Entities.Dtos.Inbox;
using Omni.App.Entities.Dtos.Monitoring;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Inbox;
using Omni.App.Entities.Responses.Inbox;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Campaigns;
using Omni.App.Persistence.Contracts.Inbox;
using Omni.App.Persistence.Contracts.Contacts;
using Omni.App.Persistence.Contracts.WhatsApp;
using Omni.App.Entities.Dtos.Notifications;
using Omni.App.Entities.Dtos.Ui;

namespace Omni.App.Business.Services.Inbox;

/// <summary>Every outbound send (member text, AI reply, template, campaign template) follows the same billing steps:
/// subscription check -> reserve (the channel's billing policy decides whether/what is billable) -> provider call
/// (release on failure) -> Message row saved with the id the reservation was keyed on. Delivery callbacks then
/// settle or release it (HandleStatusUpdateAsync). See Common/Enums/OutboundMessageKind.cs.</summary>
public class MessageService : IMessageService
{
    private readonly IConversationRepository _conversations;
    private readonly IMessageRepository _messages;
    private readonly IContactRepository _contacts;
    private readonly IWhatsAppTemplateRepository _templates;
    private readonly ICampaignRepository _campaigns;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWhatsAppService _whatsApp;
    private readonly IEnumerable<IChannelSender> _senders;
    private readonly IInboxNotifier _notifier;
    private readonly IEventLogger _events;
    private readonly INotificationService _notifications;
    private readonly IMessageBillingService _billing;
    private readonly IEntitlementService _entitlements;
    private readonly IInboxSettingsService _inboxSettings;
    private readonly IMessageTracer _tracer;
    private readonly IConversationService _conversationService;

    public MessageService(
        IConversationRepository conversations, IMessageRepository messages, IContactRepository contacts,
        IWhatsAppTemplateRepository templates, ICampaignRepository campaigns, IUnitOfWork unitOfWork, IWhatsAppService whatsApp, IEnumerable<IChannelSender> senders,
        IInboxNotifier notifier, IEventLogger events, INotificationService notifications,
        IMessageBillingService billing, IEntitlementService entitlements, IInboxSettingsService inboxSettings, IMessageTracer tracer,
        IConversationService conversationService)
    {
        _conversationService = conversationService;
        _tracer = tracer;
        _inboxSettings = inboxSettings;
        _conversations = conversations;
        _messages = messages;
        _contacts = contacts;
        _templates = templates;
        _campaigns = campaigns;
        _unitOfWork = unitOfWork;
        _whatsApp = whatsApp; // still used directly for WhatsApp-only template sends
        _senders = senders;
        _notifier = notifier;
        _events = events;
        _notifications = notifications;
        _billing = billing;
        _entitlements = entitlements;
    }

    /// <summary>Subscription gate + reservation for one outbound message. Throws EntitlementDeniedException /
    /// BillingDeniedException (both 422) before anything is sent.</summary>
    private async Task<MessageBillingHold?> PrepareBillingAsync(Conversation conversation, Guid messageId, OutboundMessageKind kind,
        string? templateCategory, Guid? campaignId, CancellationToken ct)
    {
        await _entitlements.EnsureCanSendMessagesAsync(conversation.ClientId, ct);
        return await _billing.ReserveOutboundAsync(new OutboundMessageBillingContext(
            conversation.ClientId, conversation.Channel, messageId, conversation.Id, campaignId, kind, templateCategory,
            conversation.IsServiceWindowOpen(DateTimeOffset.UtcNow), conversation.Contact?.Phone), ct);
    }

    /// <summary>PrepareBillingAsync, recording a refusal (no subscription, empty wallet) in the message trace.</summary>
    private async Task<MessageBillingHold?> PrepareTracedBillingAsync(Conversation conversation, Guid messageId, OutboundMessageKind kind,
        string? templateCategory, Guid? campaignId, CancellationToken ct)
    {
        try
        {
            return await PrepareBillingAsync(conversation, messageId, kind, templateCategory, campaignId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _tracer.Step(TraceSteps.SendBlocked, TraceOutcomes.Failed, Ids(conversation, messageId), detail: new { error = TraceText.Error(ex) });
            throw;
        }
    }

    /// <summary>The provider call, timed and recorded in the message trace (accepted with its id, or failed and why).</summary>
    private async Task<string> SendTracedAsync(Conversation conversation, Guid messageId, Func<Task<string>> send)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var externalMessageId = await send();
            _tracer.Step(TraceSteps.SendProviderAccepted, TraceOutcomes.Ok, Ids(conversation, messageId, externalMessageId),
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return externalMessageId;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _tracer.Step(TraceSteps.SendProviderFailed, TraceOutcomes.Failed, Ids(conversation, messageId),
                Stopwatch.GetElapsedTime(started).TotalMilliseconds, new { error = TraceText.Error(ex) });
            throw;
        }
    }

    private static TraceIds Ids(Conversation conversation, Guid? messageId = null, string? externalMessageId = null) =>
        new(conversation.ClientId, conversation.Id, messageId, conversation.Channel, externalMessageId);

    /// <summary>Picks the outbound adapter for a conversation's channel (WhatsApp, Telegram, ...).</summary>
    private IChannelSender ResolveSender(string channel) =>
        _senders.FirstOrDefault(s => s.Channel == channel)
        ?? throw new InvalidOperationException("inbox.errors.sendUnsupported").WithArgs(channel);

    public async Task<MessageResponse?> SendAsync(Guid clientId, Guid conversationId, SendMessageRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            throw new ArgumentException("inbox.errors.contentRequired");
        }

        var conversation = await _conversations.GetWithContactAsync(clientId, conversationId, ct);
        if (conversation is null) return null;

        var messageId = Guid.NewGuid();
        _tracer.Step(TraceSteps.SendRequested, TraceOutcomes.Ok, Ids(conversation, messageId), detail: new { kind = "member" });
        var billingHold = await PrepareTracedBillingAsync(conversation, messageId, OutboundMessageKind.Text, null, null, ct);

        // Channel-specific rules (WhatsApp's 24h window + phone, Telegram's connected bot + chat id)
        // live in the IChannelSender for this conversation's channel. The actual API call happens
        // before we touch the database — if it throws, no message row or conversation-state change
        // is left behind for a send that never went out (and the billing hold is returned).
        string externalMessageId;
        try
        {
            externalMessageId = await SendTracedAsync(conversation, messageId,
                () => ResolveSender(conversation.Channel).SendTextAsync(conversation, request.Content, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _billing.SendFailedAsync(billingHold, ct);
            await NotifyOutboundFailureAsync(clientId, conversation, ct);
            throw;
        }

        var now = DateTimeOffset.UtcNow;
        var message = new Message
        {
            Id = messageId,
            ClientId = clientId,
            ConversationId = conversationId,
            ContactId = conversation.ContactId,
            Direction = MessageDirection.Outbound,
            SenderType = MessageSenderType.Member,
            Origin = MessageOrigin.Dashboard,
            Channel = conversation.Channel,
            MessageType = string.IsNullOrWhiteSpace(request.MessageType) ? "text" : request.MessageType,
            Content = request.Content,
            ExternalMessageId = externalMessageId,
            IsAiGenerated = false,
            SentAt = now,
            CreatedAt = now,
            TraceId = TraceContext.CurrentTraceId()
        };
        _messages.Add(message);

        conversation.LastMessageAt = now;
        conversation.LastMessageDirection = MessageDirection.Outbound;
        conversation.UpdatedAt = now;

        // Members sending from the dashboard means a human is handling this conversation now.
        var modeChanged = conversation.Mode != ConversationMode.Human;
        ConversationModeSync.Apply(conversation, ConversationMode.Human, now);

        if (conversation.Contact is not null)
        {
            conversation.Contact.LastContactAt = now;
            conversation.Contact.UpdatedAt = now;
        }

        // Don't log message content into event metadata — just that a send happened and by whom.
        _events.Log(clientId, EventTypes.MemberMessageSentDashboard,
            contactId: conversation.ContactId, conversationId: conversationId, source: MessageOrigin.Dashboard);

        await _unitOfWork.SaveChangesAsync(ct);
        await _billing.SentAsync(billingHold, ct);

        await _notifier.NewMessageAsync(clientId, conversationId, message.Id, conversation.ContactId,
            MessageDirection.Outbound, MessageSenderType.Member, MessageOrigin.Dashboard, now, ct);
        if (modeChanged)
        {
            await _notifier.ConversationModeChangedAsync(clientId, conversationId, ConversationMode.Human, ct);
        }

        return InboxMapper.ToResponse(message);
    }

    public async Task<MessageResponse?> SendAiReplyAsync(Guid clientId, Guid conversationId, string content, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("inbox.errors.contentRequired");
        }

        var conversation = await _conversations.GetWithContactAsync(clientId, conversationId, ct);
        if (conversation is null) return null;

        // The mandatory re-check: a human may have taken over between the webhook that triggered
        // this AI reply and the AI actually finishing its response. Read fresh from the DB — never
        // trust the "mode":"ai" the original webhook response carried.
        if (conversation.Mode != ConversationMode.Ai)
        {
            _tracer.Step(TraceSteps.SendRejectedMode, TraceOutcomes.Skipped, Ids(conversation), detail: new { kind = "ai", mode = conversation.Mode });
            throw new ConversationNotInAiModeException(conversation.Mode);
        }

        var messageId = Guid.NewGuid();
        _tracer.Step(TraceSteps.SendRequested, TraceOutcomes.Ok, Ids(conversation, messageId), detail: new { kind = "ai" });

        // The reply itself is charged (ai_reply: included credit, then wallet) before anything is sent. A client that can't
        // pay gets no AI reply: the refusal is recorded as failed usage and the conversation goes to the team.
        var aiReplyHold = await ReserveAiReplyAsync(conversation, messageId, ct);
        MessageBillingHold? billingHold;
        try
        {
            billingHold = await PrepareTracedBillingAsync(conversation, messageId, OutboundMessageKind.Text, null, null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _billing.SendFailedAsync(aiReplyHold, ct);
            throw;
        }

        string externalMessageId;
        try
        {
            externalMessageId = await SendTracedAsync(conversation, messageId,
                () => ResolveSender(conversation.Channel).SendTextAsync(conversation, content, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _billing.SendFailedAsync(billingHold, ct);
            await _billing.SendFailedAsync(aiReplyHold, ct);
            throw;
        }

        var now = DateTimeOffset.UtcNow;
        var message = new Message
        {
            Id = messageId,
            ClientId = clientId,
            ConversationId = conversationId,
            ContactId = conversation.ContactId,
            Direction = MessageDirection.Outbound,
            SenderType = MessageSenderType.Ai,
            Origin = MessageOrigin.Ai,
            Channel = conversation.Channel,
            MessageType = "text",
            Content = content,
            ExternalMessageId = externalMessageId,
            IsAiGenerated = true,
            SentAt = now,
            CreatedAt = now,
            TraceId = TraceContext.CurrentTraceId()
        };
        _messages.Add(message);

        // AI sending a reply keeps the conversation in AI mode — never flip to human here (that's
        // what distinguishes this from SendAsync's member path).
        conversation.LastMessageAt = now;
        conversation.LastMessageDirection = MessageDirection.Outbound;
        conversation.UpdatedAt = now;

        if (conversation.Contact is not null)
        {
            conversation.Contact.LastContactAt = now;
            conversation.Contact.UpdatedAt = now;
        }

        _events.Log(clientId, EventTypes.AiMessageSent,
            contactId: conversation.ContactId, conversationId: conversationId, source: MessageOrigin.Ai);

        await _unitOfWork.SaveChangesAsync(ct);
        await _billing.SentAsync(billingHold, ct);
        await _billing.SentAsync(aiReplyHold, ct);

        await _notifier.NewMessageAsync(clientId, conversationId, message.Id, conversation.ContactId,
            MessageDirection.Outbound, MessageSenderType.Ai, MessageOrigin.Ai, now, ct);

        return InboxMapper.ToResponse(message);
    }

    /// <summary>The ai_reply reservation of an AI reply. Refused (BillingDeniedException): traced, the conversation is handed
    /// to the team (IConversationService.HandOverUnpaidAiReplyAsync), and AiReplyRefusedException tells the caller nothing
    /// was sent.</summary>
    private async Task<MessageBillingHold?> ReserveAiReplyAsync(Conversation conversation, Guid messageId, CancellationToken ct)
    {
        try
        {
            return await _billing.ReserveAiReplyAsync(conversation.ClientId, conversation.Id, messageId, ct);
        }
        catch (BillingDeniedException ex)
        {
            _tracer.Step(TraceSteps.SendBlocked, TraceOutcomes.Failed, Ids(conversation, messageId),
                detail: new { kind = "ai", billing = BillableEventTypes.AiReply, reason = ex.Reason });
            await _conversationService.HandOverUnpaidAiReplyAsync(conversation.ClientId, conversation.Id, ex.Reason, ct);
            _tracer.Step(TraceSteps.ConversationModeChanged, TraceOutcomes.Ok, Ids(conversation),
                detail: new { mode = ConversationMode.Human, reason = "ai_reply_" + ex.Reason });
            throw new AiReplyRefusedException(ex.Reason);
        }
    }

    public Task<MessageResponse?> SendTemplateAsync(Guid clientId, Guid conversationId, SendTemplateMessageRequest request, CancellationToken ct = default) =>
        SendTemplateCoreAsync(clientId, conversationId, request.WhatsAppTemplateId, request.BodyParameters ?? Array.Empty<string>(),
            MessageOrigin.Dashboard, campaignId: null, campaignRecipientId: null, ct);

    /// <summary>Campaign-origin variant of SendTemplateAsync — same WhatsApp send + Message-row logic,
    /// but stamps Origin.Campaign and links CampaignId/CampaignRecipientId instead of acting as a
    /// plain members Inbox send. Used exclusively by CampaignService.ProcessRecipientAsync.</summary>
    public Task<MessageResponse?> SendCampaignTemplateAsync(
        Guid clientId, Guid conversationId, Guid whatsAppTemplateId, IReadOnlyList<string> bodyParameters,
        Guid campaignId, Guid campaignRecipientId, CancellationToken ct = default) =>
        SendTemplateCoreAsync(clientId, conversationId, whatsAppTemplateId, bodyParameters,
            MessageOrigin.Campaign, campaignId, campaignRecipientId, ct);

    private async Task<MessageResponse?> SendTemplateCoreAsync(
        Guid clientId, Guid conversationId, Guid whatsAppTemplateId, IReadOnlyList<string> bodyParameters,
        string origin, Guid? campaignId, Guid? campaignRecipientId, CancellationToken ct)
    {
        var conversation = await _conversations.GetWithContactAsync(clientId, conversationId, ct);
        if (conversation is null) return null;

        if (conversation.Channel != ConversationChannel.WhatsApp)
        {
            throw new InvalidOperationException("templates.errors.whatsAppOnly").WithArgs(conversation.Channel);
        }

        var template = await _templates.GetReadOnlyAsync(clientId, whatsAppTemplateId, ct);
        if (template is null)
        {
            throw new ArgumentException("templates.errors.notFound");
        }
        if (template.Status != WhatsAppTemplateStatus.Approved)
        {
            throw new InvalidOperationException("templates.errors.notApproved").WithArgs(template.Name, TextArg.Value(template.Status));
        }

        var toPhone = conversation.Contact?.Phone
            ?? throw new InvalidOperationException("inbox.errors.noPhone");

        // Meta may re-categorize an approved template (CurrentCategory, from its webhook); it's billed as what it is now.
        var messageId = Guid.NewGuid();
        _tracer.Step(TraceSteps.SendRequested, TraceOutcomes.Ok, Ids(conversation, messageId),
            detail: new { kind = origin == MessageOrigin.Campaign ? "campaign" : "template", template = template.Name });
        var billingHold = await PrepareTracedBillingAsync(conversation, messageId, OutboundMessageKind.Template,
            string.IsNullOrWhiteSpace(template.CurrentCategory) ? template.Category : template.CurrentCategory, campaignId, ct);

        string externalMessageId;
        try
        {
            externalMessageId = await SendTracedAsync(conversation, messageId, () => _whatsApp.SendTemplateMessageAsync(
                clientId, toPhone, template.Name, template.Language, bodyParameters, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _billing.SendFailedAsync(billingHold, ct);
            await NotifyOutboundFailureAsync(clientId, conversation, ct);
            throw;
        }

        var now = DateTimeOffset.UtcNow;
        var message = new Message
        {
            Id = messageId,
            ClientId = clientId,
            ConversationId = conversationId,
            ContactId = conversation.ContactId,
            Direction = MessageDirection.Outbound,
            SenderType = MessageSenderType.Member,
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
            CreatedAt = now,
            TraceId = TraceContext.CurrentTraceId()
        };
        _messages.Add(message);

        // A template send (Inbox or Campaign) is still the business proactively reaching out — not
        // customer-initiated, so it must NOT touch LastCustomerMessageAt/ServiceWindowExpiresAt.
        conversation.LastMessageAt = now;
        conversation.LastMessageDirection = MessageDirection.Outbound;
        conversation.UpdatedAt = now;

        var modeChanged = conversation.Mode != ConversationMode.Human;
        ConversationModeSync.Apply(conversation, ConversationMode.Human, now);

        if (conversation.Contact is not null)
        {
            conversation.Contact.LastContactAt = now;
            conversation.Contact.UpdatedAt = now;
        }

        var eventType = origin == MessageOrigin.Campaign ? EventTypes.CampaignMessageSent : EventTypes.MemberMessageSentDashboard;
        _events.Log(clientId, eventType, contactId: conversation.ContactId, conversationId: conversationId, source: origin);

        await _unitOfWork.SaveChangesAsync(ct);
        await _billing.SentAsync(billingHold, ct);

        await _notifier.NewMessageAsync(clientId, conversationId, message.Id, conversation.ContactId,
            MessageDirection.Outbound, MessageSenderType.Member, origin, now, ct);
        if (modeChanged)
        {
            await _notifier.ConversationModeChangedAsync(clientId, conversationId, ConversationMode.Human, ct);
        }

        return InboxMapper.ToResponse(message);
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
            throw new ArgumentException("inbox.errors.invalidEventType").WithArgs(request.EventType);
        }

        if (request.EventType == IngestEventType.StatusUpdate)
        {
            return await HandleStatusUpdateAsync(request, ct);
        }

        var conversation = await _conversations.GetAsync(request.ClientId, request.ConversationId, ct);
        if (conversation is null)
        {
            return new IngestMessageResult(Found: false, Deduplicated: false, null);
        }

        // Idempotency: Meta/n8n webhook deliveries can retry. If we've already recorded this exact
        // external message for this client+channel, hand back the existing row instead of a duplicate.
        if (!string.IsNullOrWhiteSpace(request.ExternalMessageId))
        {
            var existing = await _messages.FindByExternalIdAsync(request.ClientId, request.Channel, request.ExternalMessageId, ct);
            if (existing is not null)
            {
                _tracer.Step(TraceSteps.MessageDeduplicated, TraceOutcomes.Skipped,
                    new TraceIds(request.ClientId, request.ConversationId, existing.Id, request.Channel, request.ExternalMessageId),
                    detail: new { eventType = request.EventType });
                return new IngestMessageResult(Found: true, Deduplicated: true, InboxMapper.ToResponse(existing),
                    conversation.Mode, AiEligible: false);
            }
        }

        var (direction, senderType, origin, isAiGenerated, moveToHuman, eventType) = request.EventType switch
        {
            IngestEventType.CustomerMessage => (
                MessageDirection.Inbound, MessageSenderType.Contact, MessageOrigin.CustomerFor(request.Channel),
                false, false, EventTypes.ContactMessageReceived),

            // Coexistence echo: members replied from the WhatsApp Business phone app directly. This
            // is never a customer message and must never trigger the AI — force human mode.
            IngestEventType.BusinessAppEcho => (
                MessageDirection.Outbound, MessageSenderType.Member, MessageOrigin.WhatsAppBusinessApp,
                false, true, EventTypes.MemberMessageSentWhatsAppApp),

            IngestEventType.AiMessage => (
                MessageDirection.Outbound, MessageSenderType.Ai, MessageOrigin.Ai,
                true, false, EventTypes.AiMessageSent),

            _ => throw new ArgumentException("inbox.errors.unsupportedEventType").WithArgs(request.EventType)
        };

        var now = DateTimeOffset.UtcNow;
        var message = new Message
        {
            Id = Guid.NewGuid(),
            ClientId = request.ClientId,
            ConversationId = request.ConversationId,
            ContactId = request.ContactId,
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
            CreatedAt = now,
            TraceId = TraceContext.CurrentTraceId()
        };
        _messages.Add(message);

        conversation.LastMessageAt = now;
        conversation.LastMessageDirection = direction;
        conversation.UpdatedAt = now;

        // The 24h WhatsApp service window only ever opens/extends on a genuine customer message —
        // business_app_echo, ai_message (and elsewhere: dashboard, campaign sends) must NOT touch
        // this, or member/AI could keep a window open indefinitely just by replying to themselves.
        var reopened = false;
        if (request.EventType == IngestEventType.CustomerMessage)
        {
            conversation.LastCustomerMessageAt = now;
            // The 24h window is a WhatsApp rule only — other channels (Telegram) have no such limit.
            if (conversation.Channel == ConversationChannel.WhatsApp)
            {
                conversation.ServiceWindowExpiresAt = now.AddHours(24);
            }

            // A contact writing again brings a closed conversation back into the working list. Only a genuine
            // customer message does — an echo from the Business app or an AI message never reopens one.
            reopened = ConversationStatusSync.ReopenIfNotActive(conversation, now);
        }

        var modeChanged = false;
        string? modeReason = null;
        if (moveToHuman && conversation.Mode != ConversationMode.Human)
        {
            ConversationModeSync.Apply(conversation, ConversationMode.Human, now);
            modeChanged = true;
            modeReason = "member_replied_from_business_app";
        }
        else if (request.EventType == IngestEventType.CustomerMessage && conversation.Mode == ConversationMode.Ai)
        {
            // The bot can't answer when the plan no longer includes the AI assistant, and the client may send conversations a
            // contact reopens straight to members. Either way the conversation goes to human mode (same state as a handoff),
            // so it lands in the team's needs-attention list and n8n is never called (see aiEligible below).
            var inboxSettings = await _inboxSettings.GetAsync(request.ClientId, ct);
            if (!inboxSettings.PlanIncludesAiBot || (reopened && !inboxSettings.AiBotAnswersReopened))
            {
                ConversationModeSync.Apply(conversation, ConversationMode.Human, now);
                modeChanged = true;
                modeReason = inboxSettings.PlanIncludesAiBot ? "reopened_to_human" : "plan_without_ai_assistant";
                _events.Log(request.ClientId, EventTypes.HumanHandoff, contactId: request.ContactId,
                    conversationId: request.ConversationId, source: MessageOrigin.System,
                    metadataJson: inboxSettings.PlanIncludesAiBot ? "{\"reason\":\"reopened_to_human\"}" : "{\"reason\":\"plan_without_ai_assistant\"}");
            }
        }

        var contact = await _contacts.GetByIdAsync(request.ContactId, ct);
        if (contact is not null)
        {
            contact.LastContactAt = now;
            contact.UpdatedAt = now;
        }

        // CAMPAIGN_REPLY: a genuine customer reply, attributed to whichever campaign send is the most recent
        // one still awaiting a reply (RepliedAt null) for this contact. Persisting RepliedAt here (once) is what
        // makes this fire only on the FIRST reply after that send — CampaignService's own read-time stat
        // computation is unaffected, this just also writes the column its doc comment already anticipated.
        CampaignRecipient? repliedCampaignRecipient = null;
        if (request.EventType == IngestEventType.CustomerMessage)
        {
            repliedCampaignRecipient = await _campaigns.FindLatestAwaitingReplyAsync(request.ClientId, request.ContactId, ct);
            if (repliedCampaignRecipient is not null)
            {
                repliedCampaignRecipient.RepliedAt = now;
                repliedCampaignRecipient.UpdatedAt = now;
            }
        }

        _events.Log(request.ClientId, eventType,
            contactId: request.ContactId, conversationId: request.ConversationId, source: origin);
        if (reopened)
        {
            _events.Log(request.ClientId, EventTypes.ConversationReopened,
                contactId: request.ContactId, conversationId: request.ConversationId, source: origin);
        }

        await _unitOfWork.SaveChangesAsync(ct);

        var ids = new TraceIds(request.ClientId, request.ConversationId, message.Id, request.Channel, request.ExternalMessageId);
        _tracer.Step(TraceSteps.MessageSaved, TraceOutcomes.Ok, ids,
            detail: new { eventType = request.EventType, direction, sender = senderType, type = message.MessageType });
        if (reopened) _tracer.Step(TraceSteps.ConversationReopened, TraceOutcomes.Ok, ids);
        if (modeChanged) _tracer.Step(TraceSteps.ConversationModeChanged, TraceOutcomes.Ok, ids, detail: new { mode = ConversationMode.Human, reason = modeReason });

        await _notifier.NewMessageAsync(request.ClientId, request.ConversationId, message.Id, request.ContactId,
            direction, senderType, origin, now, ct);
        _tracer.Step(TraceSteps.InboxNotified, TraceOutcomes.Ok, ids);
        if (modeChanged)
        {
            await _notifier.ConversationModeChangedAsync(request.ClientId, request.ConversationId, ConversationMode.Human, ct);
        }
        if (reopened)
        {
            // Other open Inboxes move the conversation out of the Closed tab right away.
            await _notifier.ConversationUpdatedAsync(request.ClientId, request.ConversationId, ct);
        }

        if (request.ContactWasNewlyCreated)
        {
            await _notifications.CreateAsync(request.ClientId, NotificationType.NewContact,
                NotificationText.Of("notifications.newContact.title", "notifications.newContact.message",
                    string.IsNullOrWhiteSpace(contact?.FullName) ? TextArg.Of("notifications.anyone.newContact") : contact!.FullName),
                contactId: request.ContactId, conversationId: request.ConversationId,
                link: $"/inbox?conversationId={request.ConversationId}", ct: ct);
        }

        if (repliedCampaignRecipient is not null)
        {
            var campaignName = repliedCampaignRecipient.Campaign?.Name;
            object who = string.IsNullOrWhiteSpace(contact?.FullName) ? TextArg.Of("notifications.anyone.contact") : contact!.FullName;
            await _notifications.CreateAsync(request.ClientId, NotificationType.CampaignReply,
                string.IsNullOrWhiteSpace(campaignName)
                    ? NotificationText.Of("notifications.campaignReply.title", "notifications.campaignReply.message", who)
                    : NotificationText.Of("notifications.campaignReply.title", "notifications.campaignReply.messageNamed", who, campaignName),
                contactId: request.ContactId, conversationId: request.ConversationId,
                link: $"/inbox?conversationId={request.ConversationId}", ct: ct);
        }

        // The strict AI-trigger rule (see IngestMessageResult's doc comment): only a genuine
        // customer message, in a conversation currently in AI mode, with a message type the AI is
        // equipped to handle. Media/location/contact/reaction messages are saved but never reach
        // the AI "initially" per the event-routing table — extend AiEligibleMessageTypes if/when
        // the AI assistant gains multimodal handling.
        var aiEligible = request.EventType == IngestEventType.CustomerMessage
            && conversation.Mode == ConversationMode.Ai
            && AiEligibleMessageTypes.Contains(message.MessageType);
        if (request.EventType == IngestEventType.CustomerMessage && !aiEligible)
        {
            // Why the bot won't see this message: the most specific reason first.
            var reason = modeReason ?? (conversation.Mode != ConversationMode.Ai ? "conversation_in_human_mode" : "message_type_not_supported");
            _tracer.Step(TraceSteps.BotSkipped, TraceOutcomes.Skipped, ids, detail: new { reason, type = message.MessageType });
        }

        return new IngestMessageResult(Found: true, Deduplicated: false, InboxMapper.ToResponse(message),
            conversation.Mode, aiEligible);
    }

    /// <summary>Customer message types the AI assistant is allowed to see — see IngestMessageResult.AiEligible.</summary>
    private static readonly HashSet<string> AiEligibleMessageTypes = new(StringComparer.OrdinalIgnoreCase) { "text", "interactive" };
    // (Telegram commands like /start arrive as message type "command" and media as "image"/"audio"/…,
    // so they're saved to the Inbox but never reach the AI.)

    private async Task<IngestMessageResult> HandleStatusUpdateAsync(IngestMessageRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ExternalMessageId))
        {
            throw new ArgumentException("inbox.errors.externalIdRequired");
        }

        var message = await _messages.FindByExternalIdAsync(request.ClientId, request.Channel, request.ExternalMessageId, ct);
        if (message is null)
        {
            return new IngestMessageResult(Found: false, Deduplicated: false, null);
        }

        // Providers retry callbacks and don't guarantee order ("read" can arrive before "delivered").
        // A repeat of the status the message already has is a no-op, and an older step arriving late
        // (delivered after read) only fills its own timestamp — it never moves the status backwards.
        var incoming = request.DeliveryStatus?.ToLowerInvariant();
        if (incoming is not null && string.Equals(message.DeliveryStatus, incoming, StringComparison.OrdinalIgnoreCase))
        {
            // Still offered to billing: settling/releasing is idempotent, and this lets a retried callback finish a
            // settlement that failed the first time.
            await _billing.DeliveryStatusChangedAsync(message, incoming, ct);
            return new IngestMessageResult(Found: true, Deduplicated: true, InboxMapper.ToResponse(message), AiEligible: false);
        }
        var isLateStep = ProgressRank(incoming) > 0 && ProgressRank(incoming) < ProgressRank(message.DeliveryStatus);

        // Not a new message — just update delivery status and tell the Inbox. Never touches AI/mode.
        var occurredAt = request.OccurredAt ?? DateTimeOffset.UtcNow;
        // Captured before the switch so OUTBOUND_MESSAGE_FAILED only fires on the FIRST time this message is
        // reported failed — a retried/duplicate "failed" webhook for the same message must not re-notify.
        var justFailed = message.Direction == MessageDirection.Outbound && message.FailedAt is null
                          && string.Equals(request.DeliveryStatus, "failed", StringComparison.OrdinalIgnoreCase);
        if (!isLateStep)
        {
            message.DeliveryStatus = request.DeliveryStatus;
        }
        switch (incoming)
        {
            case "sent":
                message.SentAt ??= occurredAt;
                break;
            case "delivered":
                message.DeliveredAt ??= occurredAt;
                break;
            case "read":
                message.ReadAt ??= occurredAt;
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
        if (message.CampaignRecipientId.HasValue && !isLateStep)
        {
            var recipient = await _campaigns.GetRecipientAsync(message.CampaignRecipientId.Value, ct);
            if (recipient is not null)
            {
                ApplyDeliveryStatusToRecipient(recipient, request.DeliveryStatus, request.FailureCode, request.FailureReason);
            }
        }

        await _unitOfWork.SaveChangesAsync(ct);

        // Usage billing: the channel's policy turns the status into settle / release / nothing (never throws).
        await _billing.DeliveryStatusChangedAsync(message, incoming, ct);

        if (!isLateStep)
        {
            await _notifier.MessageStatusUpdatedAsync(
                request.ClientId, message.ConversationId, message.Id, request.DeliveryStatus ?? "unknown", occurredAt, ct);
        }

        if (justFailed)
        {
            var contactName = await _contacts.GetFullNameAsync(message.ContactId, ct);
            await _notifications.CreateAsync(request.ClientId, NotificationType.OutboundMessageFailed, SendFailedText(contactName),
                contactId: message.ContactId, conversationId: message.ConversationId,
                link: $"/inbox?conversationId={message.ConversationId}", ct: ct);
        }

        // Status updates are explicitly excluded from the AI trigger — never eligible.
        return new IngestMessageResult(Found: true, Deduplicated: false, InboxMapper.ToResponse(message), AiEligible: false);
    }

    /// <summary>Order of the normal delivery progression; 0 for anything outside it (failed, deleted, unknown),
    /// which is never treated as "late".</summary>
    private static int ProgressRank(string? status) => status?.ToLowerInvariant() switch
    {
        "sent" => 1,
        "delivered" => 2,
        "read" => 3,
        _ => 0
    };

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

    /// <summary>OUTBOUND_MESSAGE_FAILED for a send that failed synchronously (IChannelSender/IWhatsAppService threw
    /// before any Message row existed). The exception's own message is never surfaced here — members see an
    /// operational sentence, not a raw technical error (Application logs still have the real exception).</summary>
    private Task NotifyOutboundFailureAsync(Guid clientId, Conversation conversation, CancellationToken ct)
    {
        var who = conversation.Contact?.FullName;
        return _notifications.CreateAsync(clientId, NotificationType.OutboundMessageFailed, SendFailedText(who),
            contactId: conversation.ContactId, conversationId: conversation.Id,
            link: $"/inbox?conversationId={conversation.Id}", ct: ct);
    }

    private static NotificationText SendFailedText(string? contactName) => string.IsNullOrWhiteSpace(contactName)
        ? NotificationText.Of("notifications.sendFailed.title", "notifications.sendFailed.messageNoName")
        : NotificationText.Of("notifications.sendFailed.title", "notifications.sendFailed.message", contactName);
}
