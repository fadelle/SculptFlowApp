using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.Inbox;

namespace PlasticSurgery.Business.Mappers.Inbox;

public static class InboxMapper
{
    public static ConversationResponse ToResponse(Conversation c) => new(
        c.Id, c.ClinicId, c.LeadId, c.Channel, c.Status, c.Mode, c.AiEnabled, c.HumanTakeover,
        c.LastMessageAt, c.LastMessageDirection, c.ServiceWindowExpiresAt,
        c.IsServiceWindowOpen(DateTimeOffset.UtcNow), c.CreatedAt
    );

    public static MessageResponse ToResponse(Message m) => new(
        m.Id, m.ConversationId, m.LeadId, m.Direction, m.SenderType, m.Origin, m.Channel, m.MessageType,
        m.Content, m.DeliveryStatus, m.IsAiGenerated, m.WhatsAppTemplateId, m.CampaignId,
        m.SentAt, m.ReceivedAt, m.CreatedAt,
        m.DeliveredAt, m.ReadAt, m.FailedAt, m.DeletedAt, m.FailureCode, m.FailureReason, m.MetadataJson
    );
}
