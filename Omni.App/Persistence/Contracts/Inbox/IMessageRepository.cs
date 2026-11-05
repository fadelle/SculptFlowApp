using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Inbox;

public interface IMessageRepository
{
    void Add(Message message);

    /// <summary>Oldest first.</summary>
    Task<IReadOnlyList<Message>> ListForConversationAsync(Guid conversationId, CancellationToken ct = default);

    /// <summary>Tracked. The provider's id is unique per client and channel.</summary>
    Task<Message?> FindByExternalIdAsync(Guid clientId, string channel, string externalMessageId, CancellationToken ct = default);

    /// <summary>Whether the conversation has any message (either way) other than <paramref name="messageId"/> from
    /// <paramref name="since"/> on: a reply, a handoff note, or a newer customer message.</summary>
    Task<bool> HasMessagesSinceAsync(Guid conversationId, DateTimeOffset since, Guid messageId, CancellationToken ct = default);

    Task<string?> GetExternalIdAsync(Guid messageId, CancellationToken ct = default);

    /// <summary>One message of this client, untracked; null when it doesn't exist or belongs to another client.</summary>
    Task<Message?> GetForClientReadOnlyAsync(Guid clientId, Guid messageId, CancellationToken ct = default);

    /// <summary>When each inbound WhatsApp-customer message landed, for the given conversations.</summary>
    Task<IReadOnlyList<(Guid ConversationId, DateTimeOffset CreatedAt)>> ListInboundCustomerMessageTimesAsync(Guid clientId,
        IReadOnlyCollection<Guid> conversationIds, CancellationToken ct = default);

    Task<IReadOnlyList<Message>> ListByIdsReadOnlyAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default);

    /// <summary>The conversation's latest inbound (customer) message id, or null.</summary>
    Task<Guid?> GetLatestInboundIdAsync(Guid conversationId, CancellationToken ct = default);

    /// <summary>Waits (up to <paramref name="wait"/>) for a lock on one AI reply's message id, so a retried reply call waits for
    /// the first one and then finds its message instead of sending again (QA t2-billing-02). A Postgres session advisory lock
    /// on this context's connection, released on dispose. Null when it couldn't be taken in time.</summary>
    Task<IAsyncDisposable?> LockReplyAsync(Guid messageId, TimeSpan wait, CancellationToken ct = default);
}
