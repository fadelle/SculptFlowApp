using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Billing;

namespace Omni.App.Persistence.Contracts.PlatformAdmin;

public interface ILeadAdminRepository
{
    Task<PagedResponse<LeadRow>> ListLeadsAsync(Guid? clientId, string? status, string? search, int page, CancellationToken ct = default);

    Task<Lead?> GetLeadAsync(Guid id, CancellationToken ct = default);

    Task<List<EventLog>> LeadEventsAsync(Guid leadId, CancellationToken ct = default);

    Task<PagedResponse<ConversationRow>> ListConversationsAsync(Guid? clientId, Guid? leadId, string? channel, string? mode,
        string? status, int page, CancellationToken ct = default);

    Task<Conversation?> GetConversationAsync(Guid id, CancellationToken ct = default);

    Task<List<Message>> MessagesAsync(Guid conversationId, int take = 500, CancellationToken ct = default);

    Task<PagedResponse<MessageRow>> ListMessagesAsync(Guid? clientId, bool failedOnly, string? sender, string? search,
        int page, CancellationToken ct = default);

    Task<PagedResponse<BookingRow>> ListBookingsAsync(Guid? clientId, Guid? leadId, string? status, bool upcomingOnly,
        int page, CancellationToken ct = default);

    /// <summary>Tracked.</summary>
    Task<Lead?> GetLeadForUpdateAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tracked.</summary>
    Task<Conversation?> GetConversationForUpdateAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tracked.</summary>
    Task<Booking?> GetBookingForUpdateAsync(Guid id, CancellationToken ct = default);
}
