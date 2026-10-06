namespace PlasticSurgery.Entities.Responses.Notifications;

/// <summary>GET /api/notifications — newest first, plus the clinic's current unread count (so the bell badge and
/// the list agree without a second round trip).</summary>
public record NotificationListResponse(IReadOnlyList<NotificationResponse> Items, int UnreadCount, int TotalCount);
