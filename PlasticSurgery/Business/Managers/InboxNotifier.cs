using Microsoft.AspNetCore.SignalR;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Entities.Responses.Notifications;
using PlasticSurgery.Hubs;

namespace PlasticSurgery.Business.Managers;

public class InboxNotifier : IInboxNotifier
{
    private readonly IHubContext<InboxHub> _hub;

    public InboxNotifier(IHubContext<InboxHub> hub)
    {
        _hub = hub;
    }

    private IClientProxy ClinicGroup(Guid clinicId) => _hub.Clients.Group(InboxHub.ClinicGroup(clinicId));

    public Task NewMessageAsync(Guid clinicId, Guid conversationId, Guid messageId, Guid leadId,
        string direction, string senderType, string origin, DateTimeOffset createdAt, CancellationToken ct = default) =>
        ClinicGroup(clinicId).SendAsync("NewMessage", new
        {
            conversationId,
            messageId,
            leadId,
            direction,
            senderType,
            origin,
            createdAt
        }, ct);

    public Task MessageStatusUpdatedAsync(Guid clinicId, Guid conversationId, Guid messageId, string deliveryStatus, DateTimeOffset updatedAt, CancellationToken ct = default) =>
        ClinicGroup(clinicId).SendAsync("MessageStatusUpdated", new { conversationId, messageId, deliveryStatus, updatedAt }, ct);

    public Task ConversationUpdatedAsync(Guid clinicId, Guid conversationId, CancellationToken ct = default) =>
        ClinicGroup(clinicId).SendAsync("ConversationUpdated", new { conversationId }, ct);

    public Task NotificationCreatedAsync(Guid clinicId, NotificationResponse notification, CancellationToken ct = default) =>
        ClinicGroup(clinicId).SendAsync("NotificationCreated", notification, ct);

    public Task AppointmentChangedAsync(Guid clinicId, Guid appointmentId, string change, CancellationToken ct = default) =>
        ClinicGroup(clinicId).SendAsync("AppointmentChanged", new { appointmentId, change }, ct);

    public Task ConversationModeChangedAsync(Guid clinicId, Guid conversationId, string mode, CancellationToken ct = default) =>
        ClinicGroup(clinicId).SendAsync("ConversationModeChanged", new { conversationId, mode }, ct);

    public Task WhatsAppTemplateUpdatedAsync(Guid clinicId, Guid templateId, string name, string status,
        string? rejectionReason, DateTimeOffset updatedAt, CancellationToken ct = default) =>
        ClinicGroup(clinicId).SendAsync("WhatsAppTemplateUpdated",
            new { templateId, name, status, rejectionReason, updatedAt }, ct);

    public Task WhatsAppHealthUpdatedAsync(Guid clinicId, Guid connectionId, string healthLevel, string? accountStatus,
        string? accountReviewStatus, string? phoneQualityRating, string? phoneStatus, string? problemMessage,
        DateTimeOffset updatedAt, CancellationToken ct = default) =>
        ClinicGroup(clinicId).SendAsync("WhatsAppHealthUpdated", new
        {
            connectionId, healthLevel, accountStatus, accountReviewStatus,
            phoneQualityRating, phoneStatus, problemMessage, updatedAt
        }, ct);
}
