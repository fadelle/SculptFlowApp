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

    public ConversationService(IConversationRepository conversations, IMessageRepository messages, IContactRepository contacts,
        IUnitOfWork unitOfWork, IInboxNotifier notifier, IEventLogger events, INotificationService notifications,
        IInboxSettingsService inboxSettings, IBusinessTypeService businessTypes)
    {
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
            throw new ArgumentException($"Invalid channel '{request.Channel}'.", nameof(request));
        }
        if (await _contacts.GetAsync(request.ClientId, request.ContactId, ct) is null)
        {
            throw new KeyNotFoundException("Contact not found.");
        }

        return await CreateCoreAsync(request, ct);
    }

    /// <summary>Creates the row without the contact lookup — for callers that just resolved or created the contact themselves.</summary>
    private async Task<ConversationResponse> CreateCoreAsync(CreateConversationRequest request, CancellationToken ct)
    {
        if (!ConversationChannel.All.Contains(request.Channel))
        {
            throw new ArgumentException($"Invalid channel '{request.Channel}'.", nameof(request));
        }

        var conversation = AddNew(request, await _inboxSettings.InitialModeAsync(request.ClientId, ct));
        await _unitOfWork.SaveChangesAsync(ct);
        return InboxMapper.ToResponse(conversation);
    }

    /// <summary><paramref name="mode"/> comes from IInboxSettingsService.InitialModeAsync: human when the plan lacks the AI
    /// agent or the client turned the bot off for new conversations.</summary>
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

        return await CreateCoreAsync(new CreateConversationRequest(clientId, contactId, channel, ExternalThreadId: null), ct);
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
            throw new ArgumentException($"Invalid channel '{channel}'.", nameof(channel));
        }
        var created = AddNew(new CreateConversationRequest(clientId, contactId, channel, externalThreadId),
            await _inboxSettings.InitialModeAsync(clientId, ct));
        if (await _unitOfWork.TrySaveChangesAsync(ct))
        {
            return InboxMapper.ToResponse(created);
        }

        // A concurrent delivery created the same thread first — our pending row was dropped; use theirs.
        var winner = await _conversations.FindByExternalThreadAsync(clientId, channel, externalThreadId, ct)
            ?? throw new InvalidOperationException("Conversation conflict reported but the existing conversation was not found.");
        return InboxMapper.ToResponse(winner);
    }

    public async Task<(ConversationResponse Conversation, IReadOnlyList<MessageResponse> Messages)?> GetByIdWithMessagesAsync(
        Guid clientId, Guid id, CancellationToken ct = default)
    {
        var conversation = await _conversations.GetAsync(clientId, id, ct);

        if (conversation is null) return null;

        var messages = await _messages.ListForConversationAsync(id, ct);

        return (InboxMapper.ToResponse(conversation), messages.Select(InboxMapper.ToResponse).ToList());
    }

    public async Task<MessageResponse?> AddMessageAsync(Guid clientId, Guid conversationId, CreateMessageRequest request, CancellationToken ct = default)
    {
        if (!MessageDirection.Inbound.Equals(request.Direction) && !MessageDirection.Outbound.Equals(request.Direction))
        {
            throw new ArgumentException($"Invalid message direction '{request.Direction}'.", nameof(request));
        }
        if (request.SenderType is not (MessageSenderType.Contact or MessageSenderType.Ai or MessageSenderType.Member or MessageSenderType.System))
        {
            throw new ArgumentException($"Invalid sender type '{request.SenderType}'.", nameof(request));
        }

        var conversation = await _conversations.GetAsync(clientId, conversationId, ct);

        if (conversation is null) return null;

        var now = DateTimeOffset.UtcNow;

        var message = new Message
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            ConversationId = conversationId,
            ContactId = conversation.ContactId,
            Direction = request.Direction,
            SenderType = request.SenderType,
            Channel = request.Channel,
            MessageType = string.IsNullOrWhiteSpace(request.MessageType) ? "text" : request.MessageType,
            Content = request.Content,
            ExternalMessageId = request.ExternalMessageId,
            IsAiGenerated = request.IsAiGenerated,
            Origin = request.Origin ?? InferOrigin(request.Direction, request.SenderType, request.Channel),
            SentAt = request.Direction == MessageDirection.Outbound ? now : null,
            ReceivedAt = request.Direction == MessageDirection.Inbound ? now : null,
            CreatedAt = now,
            TraceId = TraceContext.CurrentTraceId()
        };

        _messages.Add(message);

        conversation.LastMessageAt = now;
        conversation.LastMessageDirection = request.Direction;
        conversation.UpdatedAt = now;

        // A contact's message reopens a closed conversation (same rule as MessageService.IngestAsync).
        var reopened = request.Direction == MessageDirection.Inbound && ConversationStatusSync.ReopenIfNotActive(conversation, now);
        if (reopened)
        {
            _events.Log(clientId, EventTypes.ConversationReopened, conversationId: conversationId, source: message.Origin);
        }

        // Same reopen routing as MessageService.IngestAsync: the client (or its plan) may send reopened conversations to members.
        var handedToHuman = reopened && conversation.Mode == ConversationMode.Ai
            && !(await _inboxSettings.GetAsync(clientId, ct)).AiBotAnswersReopened;
        if (handedToHuman)
        {
            ConversationModeSync.Apply(conversation, ConversationMode.Human, now);
        }

        var contact = await _contacts.GetByIdAsync(conversation.ContactId, ct);
        if (contact is not null)
        {
            contact.LastContactAt = now;
            contact.UpdatedAt = now;
            // The first reply moves a contact out of the entry stage to the next open stage ("new" → "contacted" in both
            // built-in types); a type with a single open stage keeps the contact where it is.
            if (request.Direction == MessageDirection.Outbound
                && (await _businessTypes.GetPipelineAsync(clientId, ct)).NextAfterEntry(contact.StageKey) is { } next)
            {
                contact.StageKey = next;
            }
        }

        await _unitOfWork.SaveChangesAsync(ct);
        if (reopened) await _notifier.ConversationUpdatedAsync(clientId, conversationId, ct);
        if (handedToHuman) await _notifier.ConversationModeChangedAsync(clientId, conversationId, ConversationMode.Human, ct);
        return InboxMapper.ToResponse(message);
    }

    public async Task<(IReadOnlyList<ConversationListRow> Items, int TotalCount)> ListAsync(
        Guid clientId, int skip, int take, CancellationToken ct = default)
    {
        return await _conversations.ListInboxAsync(clientId, skip, take, ct);
    }

    public async Task<IReadOnlyList<MessageResponse>> GetMessagesAsync(Guid clientId, Guid conversationId, CancellationToken ct = default)
    {
        var belongs = await _conversations.ExistsAsync(clientId, conversationId, ct);
        if (!belongs) return Array.Empty<MessageResponse>();

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
        // Without the AI assistant in the plan nothing would answer, so the conversation must stay with members.
        if (!(await _inboxSettings.GetAsync(clientId, ct)).PlanIncludesAiBot)
        {
            throw new EntitlementDeniedException(EntitlementKeys.AiAssistant, "Your plan doesn't include the AI assistant.");
        }
        return await SetModeAsync(clientId, conversationId, ConversationMode.Ai, EventTypes.ReturnedToAi, "dashboard", "{}", ct);
    }

    public async Task<ConversationResponse?> HandoffToHumanAsync(Guid clientId, Guid conversationId, string? reason, CancellationToken ct = default)
    {
        // Read the mode BEFORE the change so a HANDOFF notification only fires on a REAL ai->human transition —
        // the AI calling this tool twice in a row (or on an already-human conversation) must not double-notify.
        var wasAlreadyHuman = await _conversations.IsInHumanModeAsync(clientId, conversationId, ct);

        var result = await SetModeAsync(clientId, conversationId, ConversationMode.Human, EventTypes.HumanHandoff,
            MessageOrigin.Ai, System.Text.Json.JsonSerializer.Serialize(new { reason }), ct);

        // The contact now needs a person: move it to the needs-human stage (it stays put when its stage already has that
        // category, e.g. a second needs-human stage such as "Medical question").
        if (result is not null && await _contacts.GetByIdAsync(result.ContactId, ct) is { } contact
            && (await _businessTypes.GetPipelineAsync(clientId, ct)).MoveTo(contact.StageKey, StageCategory.NeedsHuman) is { } stage
            && stage != contact.StageKey)
        {
            contact.StageKey = stage;
            contact.UpdatedAt = DateTimeOffset.UtcNow;
            await _unitOfWork.SaveChangesAsync(ct);
        }

        if (result is not null && !wasAlreadyHuman)
        {
            var contactName = await _contacts.GetFullNameAsync(result.ContactId, ct);
            await _notifications.CreateAsync(clientId, NotificationType.Handoff,
                "AI handed off to your team",
                (string.IsNullOrWhiteSpace(contactName) ? "A conversation" : contactName) +
                (string.IsNullOrWhiteSpace(reason) ? " needs your team's attention." : $" needs your team's attention: {reason}"),
                contactId: result.ContactId, conversationId: conversationId,
                link: $"/inbox?conversationId={conversationId}", ct: ct);
        }

        return result;
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

    /// <summary>Best-effort Origin for callers of the older generic AddMessageAsync/CreateMessageRequest
    /// path that don't pass Origin explicitly — new code (MessageService) always sets it directly.</summary>
    private static string InferOrigin(string direction, string senderType, string channel)
    {
        if (senderType == MessageSenderType.Ai) return MessageOrigin.Ai;
        if (senderType == MessageSenderType.Member) return MessageOrigin.Dashboard;
        if (senderType == MessageSenderType.Contact && channel == ConversationChannel.WhatsApp) return MessageOrigin.WhatsAppCustomer;
        if (senderType == MessageSenderType.Contact && channel == ConversationChannel.Telegram) return MessageOrigin.TelegramCustomer;
        return MessageOrigin.System;
    }

    public Task<bool> BelongsToClientAsync(Guid clientId, Guid conversationId, CancellationToken ct = default) =>
        _conversations.ExistsAsync(clientId, conversationId, ct);
}
