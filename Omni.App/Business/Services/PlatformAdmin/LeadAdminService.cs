using Omni.App.Business.Contracts.Services.Bookings;
using Omni.App.Business.Contracts.Services.BusinessTypes;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Business.Contracts.Services.PlatformAdmin;
using Omni.App.Business.Mappers.PlatformAdmin;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Requests.PlatformAdmin;
using Omni.App.Entities.Responses.Billing;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.PlatformAdmin;

namespace Omni.App.Business.Services.PlatformAdmin;

public class LeadAdminService : ILeadAdminService
{
    private readonly ILeadAdminRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConversationService _conversations;
    private readonly IBookingService _bookings;
    private readonly TimeProvider _time;
    private readonly IBusinessTypeService _businessTypes;

    public LeadAdminService(ILeadAdminRepository repository, IUnitOfWork unitOfWork, IConversationService conversations,
        IBookingService bookings, TimeProvider time, IBusinessTypeService businessTypes)
    {
        _businessTypes = businessTypes;
        _repository = repository;
        _unitOfWork = unitOfWork;
        _conversations = conversations;
        _bookings = bookings;
        _time = time;
    }

    public Task<PagedResponse<LeadRow>> ListLeadsAsync(Guid? clientId, string? status, string? search, int page, CancellationToken ct) =>
        _repository.ListLeadsAsync(clientId, status, search, page, ct);

    public async Task<LeadDetail?> GetLeadAsync(Guid id, CancellationToken ct) =>
        await _repository.GetLeadAsync(id, ct) is { } lead ? PlatformAdminMapper.ToDetail(lead) : null;

    public async Task<List<LeadEventRow>> LeadEventsAsync(Guid leadId, CancellationToken ct) =>
        (await _repository.LeadEventsAsync(leadId, ct)).Select(PlatformAdminMapper.ToRow).ToList();

    public Task<PagedResponse<ConversationRow>> ListConversationsAsync(Guid? clientId, Guid? leadId, string? channel, string? mode,
        string? status, int page, CancellationToken ct) =>
        _repository.ListConversationsAsync(clientId, leadId, channel, mode, status, page, ct);

    public async Task<ConversationDetail?> GetConversationAsync(Guid id, CancellationToken ct) =>
        await _repository.GetConversationAsync(id, ct) is { } c ? PlatformAdminMapper.ToDetail(c, _time.GetUtcNow()) : null;

    public async Task<List<MessageDetail>> MessagesAsync(Guid conversationId, int take, CancellationToken ct) =>
        (await _repository.MessagesAsync(conversationId, Math.Clamp(take, 1, 1000), ct)).Select(PlatformAdminMapper.ToDetail).ToList();

    public Task<PagedResponse<MessageRow>> ListMessagesAsync(Guid? clientId, bool failedOnly, string? sender, string? search, int page,
        CancellationToken ct) =>
        _repository.ListMessagesAsync(clientId, failedOnly, sender, search, page, ct);

    public Task<PagedResponse<BookingRow>> ListBookingsAsync(Guid? clientId, Guid? leadId, string? status, bool upcomingOnly,
        int page, CancellationToken ct) =>
        _repository.ListBookingsAsync(clientId, leadId, status, upcomingOnly, page, ct);

    public async Task<PlatformAdminChange> UpdateLeadAsync(Guid id, LeadUpdate update, CancellationToken ct)
    {
        if (!LeadQualificationStatus.All.Contains(update.QualificationStatus)) throw new ArgumentException("Unknown qualification.");
        var lead = await _repository.GetLeadForUpdateAsync(id, ct) ?? throw new KeyNotFoundException("Lead not found.");
        if (!await _businessTypes.IsStageOfClientAsync(lead.ClientId, update.Status, ct))
            throw new ArgumentException("leads.errors.unknownStage").WithArgs(update.Status);
        var now = _time.GetUtcNow();
        lead.Status = update.Status;
        lead.QualificationStatus = update.QualificationStatus;
        if (lead.MarketingOptIn && !update.MarketingOptIn) lead.OptedOutAt = now;
        if (!lead.MarketingOptIn && update.MarketingOptIn) lead.OptedOutAt = null;
        lead.MarketingOptIn = update.MarketingOptIn;
        lead.Notes = string.IsNullOrWhiteSpace(update.Notes) ? null : update.Notes.Trim();
        lead.UpdatedAt = now;
        await _unitOfWork.SaveChangesAsync(ct);
        return new PlatformAdminChange(lead.ClientId);
    }

    public async Task<PlatformAdminChange> SetModeAsync(Guid id, string mode, CancellationToken ct)
    {
        if (mode != ConversationMode.Ai && mode != ConversationMode.Human) throw new ArgumentException("Mode must be ai or human.");
        var conversation = await _repository.GetConversationAsync(id, ct) ?? throw new KeyNotFoundException("Conversation not found.");
        if (conversation.Mode != mode)
        {
            _ = mode == ConversationMode.Human
                ? await _conversations.TakeOverAsync(conversation.ClientId, id, ct)
                : await _conversations.ReturnToAiAsync(conversation.ClientId, id, ct);
        }
        return new PlatformAdminChange(conversation.ClientId);
    }

    public async Task<PlatformAdminChange> SetStatusAsync(Guid id, string status, CancellationToken ct)
    {
        if (status is not (ConversationStatus.Active or ConversationStatus.Closed or ConversationStatus.Archived))
            throw new ArgumentException("Unknown conversation status.");
        var conversation = await _repository.GetConversationForUpdateAsync(id, ct) ?? throw new KeyNotFoundException("Conversation not found.");
        if (conversation.Status == status) return new PlatformAdminChange(conversation.ClientId);
        conversation.Status = status;
        conversation.UpdatedAt = _time.GetUtcNow();
        await _unitOfWork.SaveChangesAsync(ct);
        return new PlatformAdminChange(conversation.ClientId);
    }

    public async Task<PlatformAdminChange> SetBookingStatusAsync(Guid id, string status, CancellationToken ct)
    {
        var booking = await _repository.GetBookingForUpdateAsync(id, ct) ?? throw new KeyNotFoundException("Booking not found.");
        // The client's own path: validates the status and pushes the change to a connected calendar.
        if (booking.Status != status) await _bookings.UpdateStatusAsync(booking.ClientId, id, status, ct);
        return new PlatformAdminChange(booking.ClientId);
    }
}
