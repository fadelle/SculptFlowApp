using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public class ConversationService : IConversationService
{
    private readonly ApplicationDbContext _db;
    private readonly IInboxNotifier _notifier;
    private readonly IEventLogger _events;

    public ConversationService(ApplicationDbContext db, IInboxNotifier notifier, IEventLogger events)
    {
        _db = db;
        _notifier = notifier;
        _events = events;
    }

    public async Task<ConversationResponse> CreateAsync(CreateConversationRequest request, CancellationToken ct = default)
    {
        if (!ConversationChannel.All.Contains(request.Channel))
        {
            throw new ArgumentException($"Invalid channel '{request.Channel}'.", nameof(request));
        }

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

        _db.Conversations.Add(conversation);
        await _db.SaveChangesAsync(ct);
        return ToResponse(conversation);
    }

    public async Task<ConversationResponse> GetOrCreateForLeadAsync(Guid clinicId, Guid leadId, string channel, CancellationToken ct = default)
    {
        var existing = await _db.Conversations
            .FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.LeadId == leadId && c.Channel == channel, ct);
        if (existing is not null) return ToResponse(existing);

        return await CreateAsync(new CreateConversationRequest(clinicId, leadId, channel, ExternalThreadId: null), ct);
    }

    public async Task<ConversationResponse> GetOrCreateForLeadAsync(
        Guid clinicId, Guid leadId, string channel, string externalThreadId, CancellationToken ct = default)
    {
        var existing = await _db.Conversations.FirstOrDefaultAsync(
            c => c.ClinicId == clinicId && c.LeadId == leadId && c.Channel == channel, ct);
        if (existing is not null)
        {
            if (string.IsNullOrEmpty(existing.ExternalThreadId))
            {
                existing.ExternalThreadId = externalThreadId;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
            return ToResponse(existing);
        }

        try
        {
            return await CreateAsync(new CreateConversationRequest(clinicId, leadId, channel, externalThreadId), ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();
            var winner = await _db.Conversations.FirstAsync(
                c => c.ClinicId == clinicId && c.Channel == channel && c.ExternalThreadId == externalThreadId, ct);
            return ToResponse(winner);
        }
    }

    public async Task<(ConversationResponse Conversation, IReadOnlyList<MessageResponse> Messages)?> GetByIdWithMessagesAsync(
        Guid clinicId, Guid id, CancellationToken ct = default)
    {
        var conversation = await _db.Conversations
            .FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.Id == id, ct);

        if (conversation is null) return null;

        var messages = await _db.Messages
            .Where(m => m.ConversationId == id)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        return (ToResponse(conversation), messages.Select(ToResponse).ToList());
    }

    public async Task<MessageResponse?> AddMessageAsync(Guid clinicId, Guid conversationId, CreateMessageRequest request, CancellationToken ct = default)
    {
        if (!MessageDirection.Inbound.Equals(request.Direction) && !MessageDirection.Outbound.Equals(request.Direction))
        {
            throw new ArgumentException($"Invalid message direction '{request.Direction}'.", nameof(request));
        }

        var conversation = await _db.Conversations
            .FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.Id == conversationId, ct);

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

        _db.Messages.Add(message);

        conversation.LastMessageAt = now;
        conversation.LastMessageDirection = request.Direction;
        conversation.UpdatedAt = now;

        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == conversation.LeadId, ct);
        if (lead is not null)
        {
            lead.LastContactAt = now;
            lead.UpdatedAt = now;
            if (lead.Status == LeadStatus.New && request.Direction == MessageDirection.Outbound)
            {
                lead.Status = LeadStatus.Contacted;
            }
        }

        await _db.SaveChangesAsync(ct);
        return ToResponse(message);
    }

    public async Task<(IReadOnlyList<ConversationListRow> Items, int TotalCount)> ListAsync(
        Guid clinicId, int skip, int take, CancellationToken ct = default)
    {
        var query = _db.Conversations
            .Include(c => c.Lead).ThenInclude(l => l!.Procedure)
            .Where(c => c.ClinicId == clinicId);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .Skip(skip)
            .Take(take)
            .Select(c => new ConversationListRow(
                c.Id, c.LeadId, c.Lead!.FullName, c.Lead.Phone, c.Lead.Procedure != null ? c.Lead.Procedure.Name : null,
                c.Channel, c.Status, c.Mode,
                _db.Messages.Where(m => m.ConversationId == c.Id).OrderByDescending(m => m.CreatedAt).Select(m => m.Content).FirstOrDefault(),
                c.LastMessageDirection, c.LastMessageAt, c.CreatedAt))
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<MessageResponse>> GetMessagesAsync(Guid clinicId, Guid conversationId, CancellationToken ct = default)
    {
        var belongs = await _db.Conversations.AnyAsync(c => c.ClinicId == clinicId && c.Id == conversationId, ct);
        if (!belongs) return Array.Empty<MessageResponse>();

        var messages = await _db.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        return messages.Select(ToResponse).ToList();
    }

    public Task<ConversationResponse?> TakeOverAsync(Guid clinicId, Guid conversationId, CancellationToken ct = default) =>
        SetModeAsync(clinicId, conversationId, ConversationMode.Human, EventTypes.HumanHandoff, "dashboard", "{}", ct);

    public Task<ConversationResponse?> ReturnToAiAsync(Guid clinicId, Guid conversationId, CancellationToken ct = default) =>
        SetModeAsync(clinicId, conversationId, ConversationMode.Ai, EventTypes.ReturnedToAi, "dashboard", "{}", ct);

    public Task<ConversationResponse?> HandoffToHumanAsync(Guid clinicId, Guid conversationId, string? reason, CancellationToken ct = default) =>
        SetModeAsync(clinicId, conversationId, ConversationMode.Human, EventTypes.HumanHandoff,
            MessageOrigin.Ai, System.Text.Json.JsonSerializer.Serialize(new { reason }), ct);

    public async Task<ConversationResponse?> CloseAsync(Guid clinicId, Guid conversationId, CancellationToken ct = default)
    {
        var conversation = await _db.Conversations.FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.Id == conversationId, ct);
        if (conversation is null) return null;

        conversation.Status = ConversationStatus.Closed;
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        _events.Log(clinicId, EventTypes.ConversationClosed, conversationId: conversationId, source: "dashboard");

        await _db.SaveChangesAsync(ct);
        await _notifier.ConversationUpdatedAsync(clinicId, conversationId, ct);

        return ToResponse(conversation);
    }

    private async Task<ConversationResponse?> SetModeAsync(
        Guid clinicId, Guid conversationId, string mode, string eventType, string source, string metadataJson, CancellationToken ct)
    {
        var conversation = await _db.Conversations.FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.Id == conversationId, ct);
        if (conversation is null) return null;

        ConversationModeSync.Apply(conversation, mode);
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        _events.Log(clinicId, eventType, conversationId: conversationId, source: source, metadataJson: metadataJson);

        await _db.SaveChangesAsync(ct);
        await _notifier.ConversationModeChangedAsync(clinicId, conversationId, mode, ct);

        return ToResponse(conversation);
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

    internal static ConversationResponse ToResponse(Conversation c) => new(
        c.Id, c.ClinicId, c.LeadId, c.Channel, c.Status, c.Mode, c.AiEnabled, c.HumanTakeover,
        c.LastMessageAt, c.LastMessageDirection, c.ServiceWindowExpiresAt,
        c.IsServiceWindowOpen(DateTimeOffset.UtcNow), c.CreatedAt
    );

    internal static MessageResponse ToResponse(Message m) => new(
        m.Id, m.ConversationId, m.LeadId, m.Direction, m.SenderType, m.Origin, m.Channel, m.MessageType,
        m.Content, m.DeliveryStatus, m.IsAiGenerated, m.WhatsAppTemplateId, m.CampaignId,
        m.SentAt, m.ReceivedAt, m.CreatedAt,
        m.DeliveredAt, m.ReadAt, m.FailedAt, m.DeletedAt, m.FailureCode, m.FailureReason, m.MetadataJson
    );
}
