using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.Inbox;

public interface IMessageRepository
{
    void Add(Message message);

    /// <summary>Oldest first.</summary>
    Task<IReadOnlyList<Message>> ListForConversationAsync(Guid conversationId, CancellationToken ct = default);

    /// <summary>Tracked. The provider's id is unique per clinic and channel.</summary>
    Task<Message?> FindByExternalIdAsync(Guid clinicId, string channel, string externalMessageId, CancellationToken ct = default);

    Task<string?> GetExternalIdAsync(Guid messageId, CancellationToken ct = default);

    /// <summary>One message of this clinic, untracked; null when it doesn't exist or belongs to another clinic.</summary>
    Task<Message?> GetForClinicReadOnlyAsync(Guid clinicId, Guid messageId, CancellationToken ct = default);

    /// <summary>When each inbound WhatsApp-customer message landed, for the given conversations.</summary>
    Task<IReadOnlyList<(Guid ConversationId, DateTimeOffset CreatedAt)>> ListInboundCustomerMessageTimesAsync(Guid clinicId,
        IReadOnlyCollection<Guid> conversationIds, CancellationToken ct = default);

    Task<IReadOnlyList<Message>> ListByIdsReadOnlyAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default);
}
