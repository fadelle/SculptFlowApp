using Omni.App.Entities.Models;

namespace Omni.App.Entities.Responses.Notifications;

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
