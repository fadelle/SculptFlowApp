using Microsoft.AspNetCore.SignalR;
using Omni.App.Entities.Responses.Notifications;

namespace Omni.App.Business.Contracts.Managers;

/// <summary>
/// Broadcasts real-time Inbox notifications over SignalR. PostgreSQL remains the source of truth
/// for every message/conversation — these events only tell an already-connected browser that
/// something changed so it can re-fetch from the API; the payloads carry just enough to react
/// (e.g. which conversation) without the browser needing to trust their content as authoritative.
/// If a client misses an event (disconnected, reconnecting, etc.) it loses nothing — reloading the
/// Inbox re-reads current state from the database through the normal REST endpoints.
/// </summary>
public interface IInboxNotifier
{
    Task NewMessageAsync(Guid clientId, Guid conversationId, Guid messageId, Guid leadId,
        string direction, string senderType, string origin, DateTimeOffset createdAt, CancellationToken ct = default);

    Task MessageStatusUpdatedAsync(Guid clientId, Guid conversationId, Guid messageId, string deliveryStatus, DateTimeOffset updatedAt, CancellationToken ct = default);

    Task ConversationUpdatedAsync(Guid clientId, Guid conversationId, CancellationToken ct = default);

    /// <summary>Tells the client's open Bookings calendars that a booking was created, rescheduled, canceled or had its status changed, so
    /// they re-fetch the visible month. Sent only AFTER the change is committed. change: created | rescheduled | canceled | status_changed.</summary>
    Task BookingChangedAsync(Guid clientId, Guid bookingId, string change, CancellationToken ct = default);

    Task ConversationModeChangedAsync(Guid clientId, Guid conversationId, string mode, CancellationToken ct = default);

    /// <summary>Tells the client's open dashboard(s) a new Notification was created (see INotificationService) —
    /// the bell badge/list re-fetch instead of trusting the payload as authoritative, same principle as every
    /// other event here.</summary>
    Task NotificationCreatedAsync(Guid clientId, NotificationResponse notification, CancellationToken ct = default);

    /// <summary>Notifies the Templates UI (/WhatsApp/Templates) that a template's Meta-reported
    /// state changed — status, rejection reason, quality, category. See WhatsAppTemplateService.</summary>
    Task WhatsAppTemplateUpdatedAsync(Guid clientId, Guid templateId, string name, string status,
        string? rejectionReason, DateTimeOffset updatedAt, CancellationToken ct = default);

    /// <summary>Notifies the Health UI (/WhatsApp/Health) and the dashboard's health indicator that
    /// a client's WhatsApp connection health changed. See WhatsAppHealthService.</summary>
    Task WhatsAppHealthUpdatedAsync(Guid clientId, Guid connectionId, string healthLevel, string? accountStatus,
        string? accountReviewStatus, string? phoneQualityRating, string? phoneStatus, string? problemMessage,
        DateTimeOffset updatedAt, CancellationToken ct = default);
}
