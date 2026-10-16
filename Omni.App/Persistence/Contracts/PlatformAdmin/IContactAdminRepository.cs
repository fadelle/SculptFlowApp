using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Billing;

namespace Omni.App.Persistence.Contracts.PlatformAdmin;

public interface IContactAdminRepository
{
    Task<PagedResponse<ContactRow>> ListContactsAsync(Guid? clientId, string? stage, string? search, int page, CancellationToken ct = default);

    Task<Contact?> GetContactAsync(Guid id, CancellationToken ct = default);

    Task<List<EventLog>> ContactEventsAsync(Guid contactId, CancellationToken ct = default);

    Task<PagedResponse<ConversationRow>> ListConversationsAsync(Guid? clientId, Guid? contactId, string? channel, string? mode,
        string? status, int page, CancellationToken ct = default);

    Task<Conversation?> GetConversationAsync(Guid id, CancellationToken ct = default);

    Task<List<Message>> MessagesAsync(Guid conversationId, int take = 500, CancellationToken ct = default);

    Task<PagedResponse<MessageRow>> ListMessagesAsync(Guid? clientId, bool failedOnly, string? sender, string? search,
        int page, CancellationToken ct = default);

    Task<PagedResponse<BookingRow>> ListBookingsAsync(Guid? clientId, Guid? contactId, string? status, bool upcomingOnly,
        int page, CancellationToken ct = default);

    /// <summary>Tracked.</summary>
    Task<Contact?> GetContactForUpdateAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tracked.</summary>
    Task<Conversation?> GetConversationForUpdateAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tracked.</summary>
    Task<Booking?> GetBookingForUpdateAsync(Guid id, CancellationToken ct = default);
}
