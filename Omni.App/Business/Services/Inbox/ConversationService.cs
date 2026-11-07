using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.BusinessTypes;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Business.Contracts.Services.Notifications;
using Omni.App.Business.Mappers.Inbox;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Inbox;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Inbox;
using Omni.App.Entities.Responses.Inbox;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Inbox;
using Omni.App.Persistence.Contracts.Contacts;
using Omni.App.Persistence.Contracts.Channels;
using Omni.App.Entities.Dtos.Notifications;
using Omni.App.Entities.Dtos.Ui;

namespace Omni.App.Business.Services.Inbox;

public class ConversationService : IConversationService
{
    private readonly IConversationRepository _conversations;
    private readonly IMessageRepository _messages;
    private readonly IContactRepository _contacts;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IInboxNotifier _notifier;
    private readonly IEventLogger _events;
    private readonly INotificationService _notifications;
    private readonly IInboxSettingsService _inboxSettings;
    private readonly IBusinessTypeService _businessTypes;
    private readonly IChannelIntegrationRepository _channels;
    private readonly IConfigManager _config;

    /// <summary>The most messages one thread request returns, whatever was asked.</summary>
    public const int MaxThreadTake = 1000;

    public ConversationService(IConversationRepository conversations, IMessageRepository messages, IContactRepository contacts,
        IUnitOfWork unitOfWork, IInboxNotifier notifier, IEventLogger events, INotificationService notifications,
        IInboxSettingsService inboxSettings, IBusinessTypeService businessTypes, IChannelIntegrationRepository channels, IConfigManager config)
    {
        _channels = channels;
        _config = config;
        _inboxSettings = inboxSettings;
        _businessTypes = businessTypes;
        _conversations = conversations;
        _messages = messages;
        _contacts = contacts;
        _unitOfWork = unitOfWork;
        _notifier = notifier;
        _events = events;
        _notifications = notifications;
    }

    /// <summary>A member opening a conversation: the contact must be one of this client's (404 otherwise, never a foreign-key 500
    /// or a conversation tied to another client's contact).</summary>
    public async Task<ConversationResponse> CreateAsync(CreateConversationRequest request, CancellationToken ct = default)
    {
        if (!ConversationChannel.All.Contains(request.Channel))
        {
            throw new ArgumentException("inbox.errors.invalidChannel").WithArgs(request.Channel);
        }
        if (await _contacts.GetAsync(request.ClientId, request.ContactId, ct) is null)
        {
            throw new KeyNotFoundException("contacts.errors.notFound");
        }
        // A channel Omni connects (WhatsApp, Telegram, ...) must be connected: a conversation there couldn't send or receive
        // anything and would sit in the Inbox as an orphan (QA t3-inbox-19).
        if (ChannelType.All.Contains(request.Channel)
            && (await _channels.GetReadOnlyAsync(request.ClientId, request.Channel, ct))?.Status != ChannelIntegrationStatus.Connected)
        {
            throw new InvalidOperationException("inbox.errors.channelNotConnected");
        }

        return await CreateCoreAsync(request, ct);
    }

    /// <summary>Creates the row without the contact lookup — for callers that just resolved or created the contact themselves.</summary>
    private async Task<ConversationResponse> CreateCoreAsync(CreateConversationRequest request, CancellationToken ct)
    {
        if (!ConversationChannel.All.Contains(request.Channel))
        {
            throw new ArgumentException("inbox.errors.invalidChannel").WithArgs(request.Channel);
        }

        var conversation = AddNew(request, await _inboxSettings.InitialModeAsync(request.ClientId, request.Channel, ct));
        await _unitOfWork.SaveChangesAsync(ct);
        return InboxMapper.ToResponse(conversation);
    }

    /// <summary><paramref name="mode"/> comes from IInboxSettingsService.InitialModeAsync: human when the plan lacks the AI
    /// agent, the client turned the bot off for new conversations, or the channel is beyond the AI bot limit.</summary>
    private Conversation AddNew(CreateConversationRequest request, string mode)
    {
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            ClientId = request.ClientId,
            ContactId = request.ContactId,
            Channel = request.Channel,
            ExternalThreadId = request.ExternalThreadId,
            Status = ConversationStatus.Active,
            Mode = mode,
            AiEnabled = mode == ConversationMode.Ai,
            HumanTakeover = mode == ConversationMode.Human,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _conversations.Add(conversation);
        return conversation;
    }

    public async Task<ConversationResponse> GetOrCreateForContactAsync(Guid clientId, Guid contactId, string channel, CancellationToken ct = default)
    {
        var existing = await _conversations.FindForContactAsync(clientId, contactId, channel, ct);
        if (existing is not null) return InboxMapper.ToResponse(existing);

        if (!ConversationChannel.All.Contains(channel))
        {
            throw new ArgumentException("inbox.errors.invalidChannel").WithArgs(channel);
        }
        var created = AddNew(new CreateConversationRequest(clientId, contactId, channel, ExternalThreadId: null),
            await _inboxSettings.InitialModeAsync(clientId, channel, ct));
        if (await _unitOfWork.TrySaveChangesAsync(ct)) return InboxMapper.ToResponse(created);

        // Two first messages from one contact at once (QA t10-10): the unique index on WhatsApp conversations lets one win; use it.
        var winner = await _conversations.FindForContactAsync(clientId, contactId, channel, ct)
            ?? throw new InvalidOperationException("Conversation conflict reported but the existing conversation was not found."); // text-ok: internal invariant
        return InboxMapper.ToResponse(winner);
    }

    public async Task<ConversationResponse> GetOrCreateForContactAsync(
        Guid clientId, Guid contactId, string channel, string externalThreadId, CancellationToken ct = default)
    {
        var existing = await _conversations.FindForContactAsync(clientId, contactId, channel, ct);
        if (existing is not null)
        {
            if (string.IsNullOrEmpty(existing.ExternalThreadId))
            {
                existing.ExternalThreadId = externalThreadId;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                await _unitOfWork.SaveChangesAsync(ct);
            }
            return InboxMapper.ToResponse(existing);
        }

        if (!ConversationChannel.All.Contains(channel))
        {
            throw new ArgumentException("inbox.errors.invalidChannel").WithArgs(channel);
        }
        var created = AddNew(new CreateConversationRequest(clientId, contactId, channel, externalThreadId),
            await _inboxSettings.InitialModeAsync(clientId, channel, ct));
        if (await _unitOfWork.TrySaveChangesAsync(ct))
        {
            return InboxMapper.ToResponse(created);
        }

        // A concurrent delivery created the same thread first — our pending row was dropped; use theirs.
        var winner = await _conversations.FindByExternalThreadAsync(clientId, channel, externalThreadId, ct)
            ?? throw new InvalidOperationException("Conversation conflict reported but the existing conversation was not found."); // text-ok: internal invariant
        return InboxMapper.ToResponse(winner);
    }

    public async Task<(ConversationResponse Conversation, IReadOnlyList<MessageResponse> Messages, bool HasEarlier)?> GetByIdWithMessagesAsync(
        Guid clientId, Guid id, int? take = null, CancellationToken ct = default)
    {
        var conversation = await _conversations.GetAsync(clientId, id, ct);

        if (conversation is null) return null;

        // Only the newest messages (QA t3-inbox-16: every refresh re-read the whole history). The Inbox asks for as many as it
        // shows, so "Show earlier messages" and refreshes keep what is on screen.
        var (messages, hasEarlier) = await _messages.ListLatestForConversationAsync(id, Math.Clamp(take ?? _config.InboxThreadPageSize, 1, MaxThreadTake), ct);

        return (InboxMapper.ToResponse(conversation), messages.Select(InboxMapper.ToResponse).ToList(), hasEarlier);
    }

    public Task<ConversationListPage> ListAsync(Guid clientId, ConversationListRequest request, CancellationToken ct = default)
    {
        var filter = string.IsNullOrWhiteSpace(request.Filter) ? InboxListFilter.All : request.Filter.Trim().ToLowerInvariant();
        if (!InboxListFilter.Values.Contains(filter))
        {
            throw new ArgumentException("inbox.errors.unknownFilter").WithArgs(request.Filter);
        }
        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        return _conversations.ListInboxAsync(clientId, request with { Search = search, Filter = filter }, ct);
    }

    public async Task<IReadOnlyList<MessageResponse>> GetMessagesAsync(Guid clientId, Guid conversationId, CancellationToken ct = default)
    {
        // Another client's (or an unknown) conversation is "not found", never an empty list (QA t1-accounts-17).
        var belongs = await _conversations.ExistsAsync(clientId, conversationId, ct);
        if (!belongs) throw new KeyNotFoundException("inbox.errors.conversationNotFound");

        var messages = await _messages.ListForConversationAsync(conversationId, ct);

        return messages.Select(InboxMapper.ToResponse).ToList();
    }

    public async Task<bool> MarkReadAsync(Guid clientId, Guid conversationId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        // Direct update (no tracking, no updated_at bump): reading a conversation isn't a change to it.
        var rows = await _conversations.MarkReadAsync(clientId, conversationId, now, ct);
        if (rows == 0) return false;

        // Other members with the Inbox open should drop the unread marker too.
        await _notifier.ConversationUpdatedAsync(clientId, conversationId, ct);
        return true;
    }

    public Task<ConversationResponse?> TakeOverAsync(Guid clientId, Guid conversationId, CancellationToken ct = default) =>
        SetModeAsync(clientId, conversationId, ConversationMode.Human, EventTypes.HumanHandoff, "dashboard", "{}", ct);

    public async Task<ConversationResponse?> ReturnToAiAsync(Guid clientId, Guid conversationId, CancellationToken ct = default)
    {
        // Something must answer: the plan's AI assistant and an active bot on the conversation's account (Inbox settings).
        var channel = (await _conversations.GetAsync(clientId, conversationId, ct))?.Channel;
        if (channel is null) return null;
        var decision = await _inboxSettings.ResolveAsync(clientId, channel, ct);
        if (decision.BlockReason == AiBotBlockReason.PlanWithoutAi)
        {
            throw new EntitlementDeniedException(EntitlementKeys.AiAssistant, EntitlementDeniedException.NotIncludedKey)
                .WithArgs(BillingTextHelper.EntitlementArg(EntitlementKeys.AiAssistant));
        }
        if (!decision.BotAnswers)
        {
            throw new InvalidOperationException("inbox.errors.noBotOnAccount").WithArgs(ChannelNames.Brand(channel));
        }
        return await SetModeAsync(clientId, conversationId, ConversationMode.Ai, EventTypes.ReturnedToAi, "dashboard", "{}", ct);
    }

    public Task<HandoffResult?> HandoffToHumanAsync(Guid clientId, Guid conversationId, string? reason, CancellationToken ct = default) =>
        HandOffAsync(clientId, conversationId, MessageOrigin.Ai, System.Text.Json.JsonSerializer.Serialize(new { reason }),
            who => string.IsNullOrWhiteSpace(reason)
                ? NotificationText.Of("notifications.handoff.title", "notifications.handoff.message", who)
                : NotificationText.Of("notifications.handoff.title", "notifications.handoff.messageReason", who, reason), ct);

    public async Task<ConversationResponse?> HandOverUnpaidAiReplyAsync(Guid clientId, Guid conversationId, string failureReason, CancellationToken ct = default) =>
        (await HandOffAsync(clientId, conversationId, MessageOrigin.System,
            System.Text.Json.JsonSerializer.Serialize(new { reason = "ai_reply_" + failureReason }),
            who => NotificationText.Of("notifications.handoff.title",
                failureReason == UsageFailureReason.InsufficientFunds ? AiReplyNoBalanceKey : AiReplyNotPricedKey, who), ct))?.Conversation;

    public async Task<bool?> HandOverFailedAiAsync(Guid clientId, Guid conversationId, CancellationToken ct = default) =>
        (await HandOffAsync(clientId, conversationId, MessageOrigin.System,
            System.Text.Json.JsonSerializer.Serialize(new { reason = "ai_failed" }),
            who => NotificationText.Of("notifications.handoff.title", "notifications.handoff.messageAiFailed", who), ct))?.HandedOff;

    public async Task<bool?> HandOverStoppedAiAsync(Guid clientId, Guid conversationId, string? reason, CancellationToken ct = default) =>
        (await HandOffAsync(clientId, conversationId, MessageOrigin.System,
            System.Text.Json.JsonSerializer.Serialize(new { reason = "ai_stopped", detail = reason }),
            who => NotificationText.Of("notifications.handoff.title", "notifications.handoff.messageAiStopped", who), ct))?.HandedOff;

    // The hand-over notification's wording when billing refused an AI reply (no balance / no ai_reply rate).
    private const string AiReplyNoBalanceKey = "notifications.handoff.messageAiNoBalance";
    private const string AiReplyNotPricedKey = "notifications.handoff.messageAiNotPriced";

    /// <summary>Hands a conversation to the team: human mode, the contact to the needs-human stage, and one Handoff
    /// notification (worded by <paramref name="text"/> for the contact's name), all only on a real ai -> human change. The
    /// conversation row is locked meanwhile, so the AI calling this twice at once (two messages in a row, a retried
    /// workflow) changes it, notifies and lets the caller send a handoff message exactly once.</summary>
    private async Task<HandoffResult?> HandOffAsync(Guid clientId, Guid conversationId, string source, string metadataJson,
        Func<object, NotificationText> text, CancellationToken ct)
    {
        Conversation conversation;
        await using (var transaction = await _unitOfWork.BeginTransactionAsync(ct))
        {
            var locked = await _conversations.GetForUpdateAsync(clientId, conversationId, ct);
            if (locked is null) return null;
            conversation = locked;
            if (conversation.Mode == ConversationMode.Human)
            {
                await transaction.CommitAsync(ct);
                return new HandoffResult(InboxMapper.ToResponse(conversation), HandedOff: false);
            }

            var now = DateTimeOffset.UtcNow;
            ConversationModeSync.Apply(conversation, ConversationMode.Human, now);
            conversation.UpdatedAt = now;
            _events.Log(clientId, EventTypes.HumanHandoff, conversationId: conversationId, source: source, metadataJson: metadataJson);

            // The contact now needs a person: move it to the needs-human stage (it stays put when its stage already has that
            // category, e.g. a second needs-human stage such as "Medical question").
            if (await _contacts.GetByIdAsync(conversation.ContactId, ct) is { } contact
                && (await _businessTypes.GetPipelineAsync(clientId, ct)).MoveTo(contact.StageKey, StageCategory.NeedsHuman) is { } stage
                && stage != contact.StageKey)
            {
                contact.StageKey = stage;
                contact.UpdatedAt = now;
            }

            await _unitOfWork.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        await _notifier.ConversationModeChangedAsync(clientId, conversationId, ConversationMode.Human, ct);
        var contactName = await _contacts.GetFullNameAsync(conversation.ContactId, ct);
        object who = string.IsNullOrWhiteSpace(contactName) ? TextArg.Of("notifications.anyone.conversation") : contactName;
        await _notifications.CreateAsync(clientId, NotificationType.Handoff, text(who),
            contactId: conversation.ContactId, conversationId: conversationId,
            link: $"/inbox?conversationId={conversationId}", ct: ct);

        return new HandoffResult(InboxMapper.ToResponse(conversation), HandedOff: true);
    }

    public async Task<ConversationResponse?> CloseAsync(Guid clientId, Guid conversationId, CancellationToken ct = default)
    {
        var conversation = await _conversations.GetAsync(clientId, conversationId, ct);
        if (conversation is null) return null;

        conversation.Status = ConversationStatus.Closed;
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        _events.Log(clientId, EventTypes.ConversationClosed, conversationId: conversationId, source: "dashboard");

        await _unitOfWork.SaveChangesAsync(ct);
        await _notifier.ConversationUpdatedAsync(clientId, conversationId, ct);

        return InboxMapper.ToResponse(conversation);
    }

    public async Task<ConversationResponse?> ReopenAsync(Guid clientId, Guid conversationId, CancellationToken ct = default)
    {
        var conversation = await _conversations.GetAsync(clientId, conversationId, ct);
        if (conversation is null) return null;

        // Already in the working list: nothing to change, nothing to log or broadcast.
        if (!ConversationStatusSync.ReopenIfNotActive(conversation, DateTimeOffset.UtcNow))
        {
            return InboxMapper.ToResponse(conversation);
        }

        _events.Log(clientId, EventTypes.ConversationReopened, conversationId: conversationId, source: "dashboard");

        await _unitOfWork.SaveChangesAsync(ct);
        await _notifier.ConversationUpdatedAsync(clientId, conversationId, ct);

        return InboxMapper.ToResponse(conversation);
    }

    private async Task<ConversationResponse?> SetModeAsync(
        Guid clientId, Guid conversationId, string mode, string eventType, string source, string metadataJson, CancellationToken ct)
    {
        var conversation = await _conversations.GetAsync(clientId, conversationId, ct);
        if (conversation is null) return null;

        ConversationModeSync.Apply(conversation, mode);
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        _events.Log(clientId, eventType, conversationId: conversationId, source: source, metadataJson: metadataJson);

        await _unitOfWork.SaveChangesAsync(ct);
        await _notifier.ConversationModeChangedAsync(clientId, conversationId, mode, ct);

        return InboxMapper.ToResponse(conversation);
    }

    public Task<bool> BelongsToClientAsync(Guid clientId, Guid conversationId, CancellationToken ct = default) =>
        _conversations.ExistsAsync(clientId, conversationId, ct);
}
