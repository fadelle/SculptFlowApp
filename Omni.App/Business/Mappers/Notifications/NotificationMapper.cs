using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Notifications;

namespace Omni.App.Business.Mappers.Notifications;

public static class NotificationMapper
{
    public static NotificationResponse ToResponse(Notification n) => new(
        n.Id, n.Type, n.Title, n.Message, n.ContactId, n.ConversationId, n.BookingId, n.Link, n.IsRead, n.CreatedAt);
}
