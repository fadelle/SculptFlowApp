using Microsoft.AspNetCore.SignalR;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Entities.Responses.Notifications;
using Omni.App.Hubs;

namespace Omni.App.Business.Managers;

public class InboxNotifier : IInboxNotifier
{
    private readonly IHubContext<InboxHub> _hub;

    public InboxNotifier(IHubContext<InboxHub> hub)
    {
        _hub = hub;
    }

    private IClientProxy ClientGroup(Guid clientId) => _hub.Clients.Group(InboxHub.ClientGroup(clientId));

    public Task NewMessageAsync(Guid clientId, Guid conversationId, Guid messageId, Guid contactId,
        string direction, string senderType, string origin, DateTimeOffset createdAt, CancellationToken ct = default) =>
        ClientGroup(clientId).SendAsync("NewMessage", new
        {
            conversationId,
            messageId,
            contactId,
            direction,
            senderType,
            origin,
            createdAt
        }, ct);

    public Task MessageStatusUpdatedAsync(Guid clientId, Guid conversationId, Guid messageId, string deliveryStatus, DateTimeOffset updatedAt, CancellationToken ct = default) =>
        ClientGroup(clientId).SendAsync("MessageStatusUpdated", new { conversationId, messageId, deliveryStatus, updatedAt }, ct);

    public Task ConversationUpdatedAsync(Guid clientId, Guid conversationId, CancellationToken ct = default) =>
        ClientGroup(clientId).SendAsync("ConversationUpdated", new { conversationId }, ct);

    public Task NotificationCreatedAsync(Guid clientId, NotificationResponse notification, CancellationToken ct = default) =>
        ClientGroup(clientId).SendAsync("NotificationCreated", notification, ct);

    public Task BookingChangedAsync(Guid clientId, Guid bookingId, string change, CancellationToken ct = default) =>
        ClientGroup(clientId).SendAsync("BookingChanged", new { bookingId, change }, ct);

    public Task ConversationModeChangedAsync(Guid clientId, Guid conversationId, string mode, CancellationToken ct = default) =>
        ClientGroup(clientId).SendAsync("ConversationModeChanged", new { conversationId, mode }, ct);

    public Task WhatsAppTemplateUpdatedAsync(Guid clientId, Guid templateId, string name, string status,
        string? rejectionReason, DateTimeOffset updatedAt, CancellationToken ct = default) =>
        ClientGroup(clientId).SendAsync("WhatsAppTemplateUpdated",
            new { templateId, name, status, rejectionReason, updatedAt }, ct);

    public Task WhatsAppHealthUpdatedAsync(Guid clientId, Guid connectionId, string healthLevel, string? accountStatus,
        string? accountReviewStatus, string? phoneQualityRating, string? phoneStatus, string? problemMessage,
        DateTimeOffset updatedAt, CancellationToken ct = default) =>
        ClientGroup(clientId).SendAsync("WhatsAppHealthUpdated", new
        {
            connectionId, healthLevel, accountStatus, accountReviewStatus,
            phoneQualityRating, phoneStatus, problemMessage, updatedAt
        }, ct);
}
