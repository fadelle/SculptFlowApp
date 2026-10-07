using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.Notifications;

namespace PlasticSurgery.Business.Mappers.Notifications;

public static class NotificationMapper
{
    public static NotificationResponse ToResponse(Notification n) => new(
        n.Id, n.Type, n.Title, n.Message, n.LeadId, n.ConversationId, n.AppointmentId, n.Link, n.IsRead, n.CreatedAt);
}
