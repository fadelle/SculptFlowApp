using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Requests.PlatformAdmin;
using Omni.App.Entities.Responses.Billing;

namespace Omni.App.Business.Contracts.Services.PlatformAdmin;

/// <summary>Leads, conversations, messages and appointments across clients, for the admin portal.</summary>
public interface ILeadAdminService
{
    Task<PagedResponse<LeadRow>> ListLeadsAsync(Guid? clientId, string? status, string? search, int page, CancellationToken ct);

    Task<LeadDetail?> GetLeadAsync(Guid id, CancellationToken ct);

    Task<List<LeadEventRow>> LeadEventsAsync(Guid leadId, CancellationToken ct);

    Task<PagedResponse<ConversationRow>> ListConversationsAsync(Guid? clientId, Guid? leadId, string? channel, string? mode,
        string? status, int page, CancellationToken ct);

    Task<ConversationDetail?> GetConversationAsync(Guid id, CancellationToken ct);

    Task<List<MessageDetail>> MessagesAsync(Guid conversationId, int take, CancellationToken ct);

    Task<PagedResponse<MessageRow>> ListMessagesAsync(Guid? clientId, bool failedOnly, string? sender, string? search, int page,
        CancellationToken ct);

    Task<PagedResponse<AppointmentRow>> ListAppointmentsAsync(Guid? clientId, Guid? leadId, string? status, bool upcomingOnly, int page,
        CancellationToken ct);

    Task<PlatformAdminChange> UpdateLeadAsync(Guid id, LeadUpdate update, CancellationToken ct);

    /// <summary>"ai" or "human", the same as a client returning the chat to the AI or taking it over.</summary>
    Task<PlatformAdminChange> SetModeAsync(Guid id, string mode, CancellationToken ct);

    Task<PlatformAdminChange> SetStatusAsync(Guid id, string status, CancellationToken ct);

    Task<PlatformAdminChange> SetAppointmentStatusAsync(Guid id, string status, CancellationToken ct);
}
