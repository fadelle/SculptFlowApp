using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

/// <summary>
/// The clinic's notification center (the bell). A Notification row is created ONLY from a confirmed backend
/// event, never from an AI intention alone — see each hook site for the exact "already succeeded" point:
///   NEW_LEAD               — MessageService.IngestAsync, a genuinely new lead's first inbound customer message
///                             (LeadService.GetOrCreateByPhoneAsync/GetOrCreateByExternalIdAsync's WasCreated).
///   HANDOFF                 — ConversationService.HandoffToHumanAsync, after the mode is actually saved as human
///                             (not staff Take Over — that's the staff's own action, they already know).
///   APPOINTMENT_BOOKED/RESCHEDULED/CANCELLED
///                            — AppointmentService, after the appointment row is actually committed.
///   CAMPAIGN_REPLY           — MessageService.IngestAsync, the first inbound customer message after a
///                             CampaignRecipient's SentAt (persists CampaignRecipient.RepliedAt once).
///   OUTBOUND_MESSAGE_FAILED  — MessageService, a send that actually failed (IChannelSender threw before any
///                             message was persisted, or a later delivery-status webhook reported "failed").
///   INTEGRATION_UNHEALTHY    — WhatsAppHealthService.ApplyHealthEventAsync, on the transition INTO
///                             Problem/Disconnected (not repeated while it stays unhealthy).
///
/// CreateAsync never throws — a notification failing to save must never fail the real operation it's attached
/// to (see its doc comment). Broadcasting the new notification over SignalR reuses the existing clinic-scoped
/// hub (IInboxNotifier.NotificationCreatedAsync) — no second realtime channel.
/// </summary>
public interface INotificationService
{
    /// <summary>Creates and broadcasts one notification. Swallows any failure (DB or broadcast) and returns
    /// without throwing — the caller's own operation has already succeeded by the time this is called and must
    /// not be undone or interrupted by a notification-only problem.</summary>
    Task CreateAsync(
        Guid clinicId, string type, string title, string? message,
        Guid? leadId = null, Guid? conversationId = null, Guid? appointmentId = null, Guid? channelIntegrationId = null,
        string? link = null, CancellationToken ct = default);

    Task<NotificationListResponse> ListAsync(Guid clinicId, int skip, int take, CancellationToken ct = default);

    Task<int> GetUnreadCountAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Marks one notification read. No-op (not an error) if it's already read or doesn't belong to this clinic.</summary>
    Task MarkReadAsync(Guid clinicId, Guid id, CancellationToken ct = default);

    Task MarkAllReadAsync(Guid clinicId, CancellationToken ct = default);
}
