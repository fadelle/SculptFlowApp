using Omni.App.Entities.Models;

namespace Omni.App.Entities.Responses.Notifications;

public record NotificationResponse(
    Guid Id,
    string Type,
    string Title,
    string? Message,
    Guid? LeadId,
    Guid? ConversationId,
    Guid? BookingId,
    string? Link,
    bool IsRead,
    DateTimeOffset CreatedAt
);
