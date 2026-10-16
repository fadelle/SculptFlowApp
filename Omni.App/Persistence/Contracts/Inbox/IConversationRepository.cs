using Omni.App.Entities.Dtos.Inbox;
using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Inbox;

/// <summary>Conversations. Returned entities are tracked unless the method says otherwise.</summary>
public interface IConversationRepository
{
    void Add(Conversation conversation);

    Task<Conversation?> GetAsync(Guid clientId, Guid conversationId, CancellationToken ct = default);

    /// <summary>With Contact loaded (sends need its phone and update its last-contact time).</summary>
    Task<Conversation?> GetWithContactAsync(Guid clientId, Guid conversationId, CancellationToken ct = default);

    Task<Conversation?> FindForContactAsync(Guid clientId, Guid contactId, string channel, CancellationToken ct = default);

    Task<Conversation?> FindByExternalThreadAsync(Guid clientId, string channel, string externalThreadId, CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid clientId, Guid conversationId, CancellationToken ct = default);

    Task<bool> IsInHumanModeAsync(Guid clientId, Guid conversationId, CancellationToken ct = default);

    /// <summary>The Inbox list: newest activity first, with last message preview and unread count.</summary>
    Task<(IReadOnlyList<ConversationListRow> Items, int TotalCount)> ListInboxAsync(Guid clientId, int skip, int take,
        CancellationToken ct = default);

    /// <summary>Sets LastReadAt directly in the database (no updated_at bump). Returns the number of rows changed.</summary>
    Task<int> MarkReadAsync(Guid clientId, Guid conversationId, DateTimeOffset readAt, CancellationToken ct = default);
}
