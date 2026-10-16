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

public class ContactAdminService : IContactAdminService
{
    private readonly IContactAdminRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConversationService _conversations;
    private readonly IBookingService _bookings;
    private readonly TimeProvider _time;
    private readonly IBusinessTypeService _businessTypes;

    public ContactAdminService(IContactAdminRepository repository, IUnitOfWork unitOfWork, IConversationService conversations,
        IBookingService bookings, TimeProvider time, IBusinessTypeService businessTypes)
    {
        _businessTypes = businessTypes;
        _repository = repository;
        _unitOfWork = unitOfWork;
        _conversations = conversations;
        _bookings = bookings;
        _time = time;
    }

    public Task<PagedResponse<ContactRow>> ListContactsAsync(Guid? clientId, string? stage, string? search, int page, CancellationToken ct) =>
        _repository.ListContactsAsync(clientId, stage, search, page, ct);

    public async Task<ContactDetail?> GetContactAsync(Guid id, CancellationToken ct) =>
        await _repository.GetContactAsync(id, ct) is { } contact ? PlatformAdminMapper.ToDetail(contact) : null;

    public async Task<List<ContactEventRow>> ContactEventsAsync(Guid contactId, CancellationToken ct) =>
        (await _repository.ContactEventsAsync(contactId, ct)).Select(PlatformAdminMapper.ToRow).ToList();

    public Task<PagedResponse<ConversationRow>> ListConversationsAsync(Guid? clientId, Guid? contactId, string? channel, string? mode,
        string? status, int page, CancellationToken ct) =>
        _repository.ListConversationsAsync(clientId, contactId, channel, mode, status, page, ct);

    public async Task<ConversationDetail?> GetConversationAsync(Guid id, CancellationToken ct) =>
        await _repository.GetConversationAsync(id, ct) is { } c ? PlatformAdminMapper.ToDetail(c, _time.GetUtcNow()) : null;

    public async Task<List<MessageDetail>> MessagesAsync(Guid conversationId, int take, CancellationToken ct) =>
        (await _repository.MessagesAsync(conversationId, Math.Clamp(take, 1, 1000), ct)).Select(PlatformAdminMapper.ToDetail).ToList();

    public Task<PagedResponse<MessageRow>> ListMessagesAsync(Guid? clientId, bool failedOnly, string? sender, string? search, int page,
        CancellationToken ct) =>
        _repository.ListMessagesAsync(clientId, failedOnly, sender, search, page, ct);

    public Task<PagedResponse<BookingRow>> ListBookingsAsync(Guid? clientId, Guid? contactId, string? status, bool upcomingOnly,
        int page, CancellationToken ct) =>
        _repository.ListBookingsAsync(clientId, contactId, status, upcomingOnly, page, ct);

    public async Task<PlatformAdminChange> UpdateContactAsync(Guid id, ContactUpdate update, CancellationToken ct)
    {
        if (!ContactQualification.All.Contains(update.Qualification)) throw new ArgumentException("Unknown qualification.");
        var contact = await _repository.GetContactForUpdateAsync(id, ct) ?? throw new KeyNotFoundException("Contact not found.");
        if (!await _businessTypes.IsStageOfClientAsync(contact.ClientId, update.StageKey, ct))
            throw new ArgumentException("contacts.errors.unknownStage").WithArgs(update.StageKey);
        var now = _time.GetUtcNow();
        contact.StageKey = update.StageKey;
        contact.Qualification = update.Qualification;
        if (contact.MarketingOptIn && !update.MarketingOptIn) contact.OptedOutAt = now;
        if (!contact.MarketingOptIn && update.MarketingOptIn) contact.OptedOutAt = null;
        contact.MarketingOptIn = update.MarketingOptIn;
        contact.Notes = string.IsNullOrWhiteSpace(update.Notes) ? null : update.Notes.Trim();
        contact.UpdatedAt = now;
        await _unitOfWork.SaveChangesAsync(ct);
        return new PlatformAdminChange(contact.ClientId);
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
