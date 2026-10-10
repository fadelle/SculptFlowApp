using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Contracts.Services.Inbox;
using PlasticSurgery.Business.Contracts.Services.Notifications;
using PlasticSurgery.Business.Mappers.Inbox;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Helpers;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Entities.Dtos.Inbox;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Requests.Inbox;
using PlasticSurgery.Entities.Responses.Inbox;
using PlasticSurgery.Persistence.Contracts;
using PlasticSurgery.Persistence.Contracts.Inbox;
using PlasticSurgery.Persistence.Contracts.Leads;

namespace PlasticSurgery.Business.Services.Inbox;

public class ConversationService : IConversationService
{
    private readonly IConversationRepository _conversations;
    private readonly IMessageRepository _messages;
    private readonly ILeadRepository _leads;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IInboxNotifier _notifier;
    private readonly IEventLogger _events;
    private readonly INotificationService _notifications;

    public ConversationService(IConversationRepository conversations, IMessageRepository messages, ILeadRepository leads,
        IUnitOfWork unitOfWork, IInboxNotifier notifier, IEventLogger events, INotificationService notifications)
    {
        _conversations = conversations;
        _messages = messages;
        _leads = leads;
        _unitOfWork = unitOfWork;
        _notifier = notifier;
        _events = events;
        _notifications = notifications;
    }

    public async Task<ConversationResponse> CreateAsync(CreateConversationRequest request, CancellationToken ct = default)
    {
        if (!ConversationChannel.All.Contains(request.Channel))
        {
            throw new ArgumentException($"Invalid channel '{request.Channel}'.", nameof(request));
        }

        var conversation = AddNew(request);
        await _unitOfWork.SaveChangesAsync(ct);
        return InboxMapper.ToResponse(conversation);
    }

    private Conversation AddNew(CreateConversationRequest request)
    {
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            ClinicId = request.ClinicId,
            LeadId = request.LeadId,
            Channel = request.Channel,
            ExternalThreadId = request.ExternalThreadId,
            Status = ConversationStatus.Active,
            Mode = ConversationMode.Ai,
            AiEnabled = true,
            HumanTakeover = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _conversations.Add(conversation);
        return conversation;
    }

    public async Task<ConversationResponse> GetOrCreateForLeadAsync(Guid clinicId, Guid leadId, string channel, CancellationToken ct = default)
    {
        var existing = await _conversations.FindForLeadAsync(clinicId, leadId, channel, ct);
        if (existing is not null) return InboxMapper.ToResponse(existing);

        return await CreateAsync(new CreateConversationRequest(clinicId, leadId, channel, ExternalThreadId: null), ct);
    }

    public async Task<ConversationResponse> GetOrCreateForLeadAsync(
        Guid clinicId, Guid leadId, string channel, string externalThreadId, CancellationToken ct = default)
    {
        var existing = await _conversations.FindForLeadAsync(clinicId, leadId, channel, ct);
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
        var created = AddNew(new CreateConversationRequest(clinicId, leadId, channel, externalThreadId));
        if (await _unitOfWork.TrySaveChangesAsync(ct))
        {
            return InboxMapper.ToResponse(created);
        }

        // A concurrent delivery created the same thread first — our pending row was dropped; use theirs.
        var winner = await _conversations.FindByExternalThreadAsync(clinicId, channel, externalThreadId, ct)
            ?? throw new InvalidOperationException("Conversation conflict reported but the existing conversation was not found.");
        return InboxMapper.ToResponse(winner);
    }

    public async Task<(ConversationResponse Conversation, IReadOnlyList<MessageResponse> Messages)?> GetByIdWithMessagesAsync(
        Guid clinicId, Guid id, CancellationToken ct = default)
    {
        var conversation = await _conversations.GetAsync(clinicId, id, ct);

        if (conversation is null) return null;

        var messages = await _messages.ListForConversationAsync(id, ct);

        return (InboxMapper.ToResponse(conversation), messages.Select(InboxMapper.ToResponse).ToList());
    }

    public async Task<MessageResponse?> AddMessageAsync(Guid clinicId, Guid conversationId, CreateMessageRequest request, CancellationToken ct = default)
    {
        if (!MessageDirection.Inbound.Equals(request.Direction) && !MessageDirection.Outbound.Equals(request.Direction))
        {
            throw new ArgumentException($"Invalid message direction '{request.Direction}'.", nameof(request));
        }

        var conversation = await _conversations.GetAsync(clinicId, conversationId, ct);

        if (conversation is null) return null;

        var now = DateTimeOffset.UtcNow;

        var message = new Message
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            ConversationId = conversationId,
            LeadId = conversation.LeadId,
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
            CreatedAt = now
        };

        _messages.Add(message);

        conversation.LastMessageAt = now;
        conversation.LastMessageDirection = request.Direction;
        conversation.UpdatedAt = now;

        // A patient's message reopens a closed conversation (same rule as MessageService.IngestAsync).
        var reopened = request.Direction == MessageDirection.Inbound && ConversationStatusSync.ReopenIfNotActive(conversation, now);
        if (reopened)
        {
            _events.Log(clinicId, EventTypes.ConversationReopened, conversationId: conversationId, source: message.Origin);
        }

        var lead = await _leads.GetByIdAsync(conversation.LeadId, ct);
        if (lead is not null)
        {
            lead.LastContactAt = now;
            lead.UpdatedAt = now;
            if (lead.Status == LeadStatus.New && request.Direction == MessageDirection.Outbound)
            {
                lead.Status = LeadStatus.Contacted;
            }
        }

        await _unitOfWork.SaveChangesAsync(ct);
        if (reopened) await _notifier.ConversationUpdatedAsync(clinicId, conversationId, ct);
        return InboxMapper.ToResponse(message);
    }

    public async Task<(IReadOnlyList<ConversationListRow> Items, int TotalCount)> ListAsync(
        Guid clinicId, int skip, int take, CancellationToken ct = default)
    {
        return await _conversations.ListInboxAsync(clinicId, skip, take, ct);
    }

    public async Task<IReadOnlyList<MessageResponse>> GetMessagesAsync(Guid clinicId, Guid conversationId, CancellationToken ct = default)
    {
        var belongs = await _conversations.ExistsAsync(clinicId, conversationId, ct);
        if (!belongs) return Array.Empty<MessageResponse>();

        var messages = await _messages.ListForConversationAsync(conversationId, ct);

        return messages.Select(InboxMapper.ToResponse).ToList();
    }

    public async Task<bool> MarkReadAsync(Guid clinicId, Guid conversationId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        // Direct update (no tracking, no updated_at bump): reading a conversation isn't a change to it.
        var rows = await _conversations.MarkReadAsync(clinicId, conversationId, now, ct);
        if (rows == 0) return false;

        // Other staff with the Inbox open should drop the unread marker too.
        await _notifier.ConversationUpdatedAsync(clinicId, conversationId, ct);
        return true;
    }

    public Task<ConversationResponse?> TakeOverAsync(Guid clinicId, Guid conversationId, CancellationToken ct = default) =>
        SetModeAsync(clinicId, conversationId, ConversationMode.Human, EventTypes.HumanHandoff, "dashboard", "{}", ct);

    public Task<ConversationResponse?> ReturnToAiAsync(Guid clinicId, Guid conversationId, CancellationToken ct = default) =>
        SetModeAsync(clinicId, conversationId, ConversationMode.Ai, EventTypes.ReturnedToAi, "dashboard", "{}", ct);

    public async Task<ConversationResponse?> HandoffToHumanAsync(Guid clinicId, Guid conversationId, string? reason, CancellationToken ct = default)
    {
        // Read the mode BEFORE the change so a HANDOFF notification only fires on a REAL ai->human transition —
        // the AI calling this tool twice in a row (or on an already-human conversation) must not double-notify.
        var wasAlreadyHuman = await _conversations.IsInHumanModeAsync(clinicId, conversationId, ct);

        var result = await SetModeAsync(clinicId, conversationId, ConversationMode.Human, EventTypes.HumanHandoff,
            MessageOrigin.Ai, System.Text.Json.JsonSerializer.Serialize(new { reason }), ct);

        if (result is not null && !wasAlreadyHuman)
        {
            var leadName = await _leads.GetFullNameAsync(result.LeadId, ct);
            await _notifications.CreateAsync(clinicId, NotificationType.Handoff,
                "AI handed off to staff",
                (string.IsNullOrWhiteSpace(leadName) ? "A conversation" : leadName) +
                (string.IsNullOrWhiteSpace(reason) ? " needs staff attention." : $" needs staff attention: {reason}"),
                leadId: result.LeadId, conversationId: conversationId,
                link: $"/inbox?conversationId={conversationId}", ct: ct);
        }

        return result;
    }

    public async Task<ConversationResponse?> CloseAsync(Guid clinicId, Guid conversationId, CancellationToken ct = default)
    {
        var conversation = await _conversations.GetAsync(clinicId, conversationId, ct);
        if (conversation is null) return null;

        conversation.Status = ConversationStatus.Closed;
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        _events.Log(clinicId, EventTypes.ConversationClosed, conversationId: conversationId, source: "dashboard");

        await _unitOfWork.SaveChangesAsync(ct);
        await _notifier.ConversationUpdatedAsync(clinicId, conversationId, ct);

        return InboxMapper.ToResponse(conversation);
    }

    public async Task<ConversationResponse?> ReopenAsync(Guid clinicId, Guid conversationId, CancellationToken ct = default)
    {
        var conversation = await _conversations.GetAsync(clinicId, conversationId, ct);
        if (conversation is null) return null;

        // Already in the working list: nothing to change, nothing to log or broadcast.
        if (!ConversationStatusSync.ReopenIfNotActive(conversation, DateTimeOffset.UtcNow))
        {
            return InboxMapper.ToResponse(conversation);
        }

        _events.Log(clinicId, EventTypes.ConversationReopened, conversationId: conversationId, source: "dashboard");

        await _unitOfWork.SaveChangesAsync(ct);
        await _notifier.ConversationUpdatedAsync(clinicId, conversationId, ct);

        return InboxMapper.ToResponse(conversation);
    }

    private async Task<ConversationResponse?> SetModeAsync(
        Guid clinicId, Guid conversationId, string mode, string eventType, string source, string metadataJson, CancellationToken ct)
    {
        var conversation = await _conversations.GetAsync(clinicId, conversationId, ct);
        if (conversation is null) return null;

        ConversationModeSync.Apply(conversation, mode);
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        _events.Log(clinicId, eventType, conversationId: conversationId, source: source, metadataJson: metadataJson);

        await _unitOfWork.SaveChangesAsync(ct);
        await _notifier.ConversationModeChangedAsync(clinicId, conversationId, mode, ct);

        return InboxMapper.ToResponse(conversation);
    }

    /// <summary>Best-effort Origin for callers of the older generic AddMessageAsync/CreateMessageRequest
    /// path that don't pass Origin explicitly — new code (MessageService) always sets it directly.</summary>
    private static string InferOrigin(string direction, string senderType, string channel)
    {
        if (senderType == MessageSenderType.Ai) return MessageOrigin.Ai;
        if (senderType == MessageSenderType.Staff) return MessageOrigin.Dashboard;
        if (senderType == MessageSenderType.Lead && channel == ConversationChannel.WhatsApp) return MessageOrigin.WhatsAppCustomer;
        if (senderType == MessageSenderType.Lead && channel == ConversationChannel.Telegram) return MessageOrigin.TelegramCustomer;
        return MessageOrigin.System;
    }

    public Task<bool> BelongsToClinicAsync(Guid clinicId, Guid conversationId, CancellationToken ct = default) =>
        _conversations.ExistsAsync(clinicId, conversationId, ct);
}
