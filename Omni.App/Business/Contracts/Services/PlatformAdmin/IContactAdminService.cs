using Omni.App.Entities.Dtos.Contacts;
using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Requests.PlatformAdmin;
using Omni.App.Entities.Responses.Billing;

namespace Omni.App.Business.Contracts.Services.PlatformAdmin;

/// <summary>Contacts, conversations, messages and bookings across clients, for the admin portal.</summary>
public interface IContactAdminService
{
    Task<PagedResponse<ContactRow>> ListContactsAsync(Guid? clientId, string? stage, string? search, int page, CancellationToken ct);

    Task<ContactDetail?> GetContactAsync(Guid id, CancellationToken ct);

    /// <summary>Everything Omni holds about the contact (IContactDataService.ExportAnyAsync); null when unknown.</summary>
    Task<ContactExport?> ExportContactAsync(Guid id, CancellationToken ct);

    /// <summary>Deletes the contact and its data for good; null when unknown.</summary>
    Task<PlatformAdminChange?> DeleteContactAsync(Guid id, CancellationToken ct);

    Task<List<ContactEventRow>> ContactEventsAsync(Guid contactId, CancellationToken ct);

    Task<PagedResponse<ConversationRow>> ListConversationsAsync(Guid? clientId, Guid? contactId, string? channel, string? mode,
        string? status, int page, CancellationToken ct);

    Task<ConversationDetail?> GetConversationAsync(Guid id, CancellationToken ct);

    Task<List<MessageDetail>> MessagesAsync(Guid conversationId, int take, CancellationToken ct);

    Task<PagedResponse<MessageRow>> ListMessagesAsync(Guid? clientId, bool failedOnly, string? sender, string? search, int page,
        CancellationToken ct);

    Task<PagedResponse<BookingRow>> ListBookingsAsync(Guid? clientId, Guid? contactId, string? status, bool upcomingOnly, int page,
        CancellationToken ct);

    Task<PlatformAdminChange> UpdateContactAsync(Guid id, ContactUpdate update, CancellationToken ct);

    /// <summary>"ai" or "human", the same as a client returning the chat to the AI or taking it over.</summary>
    Task<PlatformAdminChange> SetModeAsync(Guid id, string mode, CancellationToken ct);

    Task<PlatformAdminChange> SetStatusAsync(Guid id, string status, CancellationToken ct);

    Task<PlatformAdminChange> SetBookingStatusAsync(Guid id, string status, CancellationToken ct);
}
