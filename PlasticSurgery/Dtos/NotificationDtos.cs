namespace PlasticSurgery.Dtos;

public record NotificationResponse(
    Guid Id,
    string Type,
    string Title,
    string? Message,
    Guid? LeadId,
    Guid? ConversationId,
    Guid? AppointmentId,
    string? Link,
    bool IsRead,
    DateTimeOffset CreatedAt
);

/// <summary>GET /api/notifications — newest first, plus the clinic's current unread count (so the bell badge and
/// the list agree without a second round trip).</summary>
public record NotificationListResponse(IReadOnlyList<NotificationResponse> Items, int UnreadCount, int TotalCount);
