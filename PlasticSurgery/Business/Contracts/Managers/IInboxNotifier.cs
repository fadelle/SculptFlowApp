using Microsoft.AspNetCore.SignalR;
using PlasticSurgery.Entities.Responses.Notifications;

namespace PlasticSurgery.Business.Contracts.Managers;

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
    Task NewMessageAsync(Guid clinicId, Guid conversationId, Guid messageId, Guid leadId,
        string direction, string senderType, string origin, DateTimeOffset createdAt, CancellationToken ct = default);

    Task MessageStatusUpdatedAsync(Guid clinicId, Guid conversationId, Guid messageId, string deliveryStatus, DateTimeOffset updatedAt, CancellationToken ct = default);

    Task ConversationUpdatedAsync(Guid clinicId, Guid conversationId, CancellationToken ct = default);

    /// <summary>Tells the clinic's open Appointments calendars that an appointment was created, rescheduled, canceled or had its status changed, so
    /// they re-fetch the visible month. Sent only AFTER the change is committed. change: created | rescheduled | canceled | status_changed.</summary>
    Task AppointmentChangedAsync(Guid clinicId, Guid appointmentId, string change, CancellationToken ct = default);

    Task ConversationModeChangedAsync(Guid clinicId, Guid conversationId, string mode, CancellationToken ct = default);

    /// <summary>Tells the clinic's open dashboard(s) a new Notification was created (see INotificationService) —
    /// the bell badge/list re-fetch instead of trusting the payload as authoritative, same principle as every
    /// other event here.</summary>
    Task NotificationCreatedAsync(Guid clinicId, NotificationResponse notification, CancellationToken ct = default);

    /// <summary>Notifies the Templates UI (/WhatsApp/Templates) that a template's Meta-reported
    /// state changed — status, rejection reason, quality, category. See WhatsAppTemplateService.</summary>
    Task WhatsAppTemplateUpdatedAsync(Guid clinicId, Guid templateId, string name, string status,
        string? rejectionReason, DateTimeOffset updatedAt, CancellationToken ct = default);

    /// <summary>Notifies the Health UI (/WhatsApp/Health) and the dashboard's health indicator that
    /// a clinic's WhatsApp connection health changed. See WhatsAppHealthService.</summary>
    Task WhatsAppHealthUpdatedAsync(Guid clinicId, Guid connectionId, string healthLevel, string? accountStatus,
        string? accountReviewStatus, string? phoneQualityRating, string? phoneStatus, string? problemMessage,
        DateTimeOffset updatedAt, CancellationToken ct = default);
}
