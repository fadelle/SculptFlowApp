using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Inbox;

public interface IMessageRepository
{
    void Add(Message message);

    /// <summary>Oldest first.</summary>
    Task<IReadOnlyList<Message>> ListForConversationAsync(Guid conversationId, CancellationToken ct = default);

    /// <summary>Tracked. The provider's id is unique per client and channel.</summary>
    Task<Message?> FindByExternalIdAsync(Guid clientId, string channel, string externalMessageId, CancellationToken ct = default);

    Task<string?> GetExternalIdAsync(Guid messageId, CancellationToken ct = default);

    /// <summary>One message of this client, untracked; null when it doesn't exist or belongs to another client.</summary>
    Task<Message?> GetForClientReadOnlyAsync(Guid clientId, Guid messageId, CancellationToken ct = default);

    /// <summary>When each inbound WhatsApp-customer message landed, for the given conversations.</summary>
    Task<IReadOnlyList<(Guid ConversationId, DateTimeOffset CreatedAt)>> ListInboundCustomerMessageTimesAsync(Guid clientId,
        IReadOnlyCollection<Guid> conversationIds, CancellationToken ct = default);

    Task<IReadOnlyList<Message>> ListByIdsReadOnlyAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default);
}
