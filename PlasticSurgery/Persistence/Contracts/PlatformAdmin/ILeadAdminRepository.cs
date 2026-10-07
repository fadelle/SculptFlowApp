using PlasticSurgery.Entities.Dtos.PlatformAdmin;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.Billing;

namespace PlasticSurgery.Persistence.Contracts.PlatformAdmin;

public interface ILeadAdminRepository
{
    Task<PagedResponse<LeadRow>> ListLeadsAsync(Guid? clinicId, string? status, string? search, int page, CancellationToken ct = default);

    Task<Lead?> GetLeadAsync(Guid id, CancellationToken ct = default);

    Task<List<EventLog>> LeadEventsAsync(Guid leadId, CancellationToken ct = default);

    Task<PagedResponse<ConversationRow>> ListConversationsAsync(Guid? clinicId, Guid? leadId, string? channel, string? mode,
        string? status, int page, CancellationToken ct = default);

    Task<Conversation?> GetConversationAsync(Guid id, CancellationToken ct = default);

    Task<List<Message>> MessagesAsync(Guid conversationId, int take = 500, CancellationToken ct = default);

    Task<PagedResponse<MessageRow>> ListMessagesAsync(Guid? clinicId, bool failedOnly, string? sender, string? search,
        int page, CancellationToken ct = default);

    Task<PagedResponse<AppointmentRow>> ListAppointmentsAsync(Guid? clinicId, Guid? leadId, string? status, bool upcomingOnly,
        int page, CancellationToken ct = default);

    /// <summary>Tracked.</summary>
    Task<Lead?> GetLeadForUpdateAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tracked.</summary>
    Task<Conversation?> GetConversationForUpdateAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tracked.</summary>
    Task<Appointment?> GetAppointmentForUpdateAsync(Guid id, CancellationToken ct = default);
}
