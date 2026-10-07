using PlasticSurgery.Entities.Dtos.PlatformAdmin;
using PlasticSurgery.Entities.Requests.PlatformAdmin;
using PlasticSurgery.Entities.Responses.Billing;

namespace PlasticSurgery.Business.Contracts.Services.PlatformAdmin;

/// <summary>Leads, conversations, messages and appointments across clinics, for the admin portal.</summary>
public interface ILeadAdminService
{
    Task<PagedResponse<LeadRow>> ListLeadsAsync(Guid? clinicId, string? status, string? search, int page, CancellationToken ct);

    Task<LeadDetail?> GetLeadAsync(Guid id, CancellationToken ct);

    Task<List<LeadEventRow>> LeadEventsAsync(Guid leadId, CancellationToken ct);

    Task<PagedResponse<ConversationRow>> ListConversationsAsync(Guid? clinicId, Guid? leadId, string? channel, string? mode,
        string? status, int page, CancellationToken ct);

    Task<ConversationDetail?> GetConversationAsync(Guid id, CancellationToken ct);

    Task<List<MessageDetail>> MessagesAsync(Guid conversationId, int take, CancellationToken ct);

    Task<PagedResponse<MessageRow>> ListMessagesAsync(Guid? clinicId, bool failedOnly, string? sender, string? search, int page,
        CancellationToken ct);

    Task<PagedResponse<AppointmentRow>> ListAppointmentsAsync(Guid? clinicId, Guid? leadId, string? status, bool upcomingOnly, int page,
        CancellationToken ct);

    Task<PlatformAdminChange> UpdateLeadAsync(Guid id, LeadUpdate update, CancellationToken ct);

    /// <summary>"ai" or "human", the same as a clinic returning the chat to the AI or taking it over.</summary>
    Task<PlatformAdminChange> SetModeAsync(Guid id, string mode, CancellationToken ct);

    Task<PlatformAdminChange> SetStatusAsync(Guid id, string status, CancellationToken ct);

    Task<PlatformAdminChange> SetAppointmentStatusAsync(Guid id, string status, CancellationToken ct);
}
