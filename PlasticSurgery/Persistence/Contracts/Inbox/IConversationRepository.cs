using PlasticSurgery.Entities.Dtos.Inbox;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.Inbox;

/// <summary>Conversations. Returned entities are tracked unless the method says otherwise.</summary>
public interface IConversationRepository
{
    void Add(Conversation conversation);

    Task<Conversation?> GetAsync(Guid clinicId, Guid conversationId, CancellationToken ct = default);

    /// <summary>With Lead loaded (sends need its phone and update its last-contact time).</summary>
    Task<Conversation?> GetWithLeadAsync(Guid clinicId, Guid conversationId, CancellationToken ct = default);

    Task<Conversation?> FindForLeadAsync(Guid clinicId, Guid leadId, string channel, CancellationToken ct = default);

    Task<Conversation?> FindByExternalThreadAsync(Guid clinicId, string channel, string externalThreadId, CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid clinicId, Guid conversationId, CancellationToken ct = default);

    Task<bool> IsInHumanModeAsync(Guid clinicId, Guid conversationId, CancellationToken ct = default);

    /// <summary>The Inbox list: newest activity first, with last message preview and unread count.</summary>
    Task<(IReadOnlyList<ConversationListRow> Items, int TotalCount)> ListInboxAsync(Guid clinicId, int skip, int take,
        CancellationToken ct = default);

    /// <summary>Sets LastReadAt directly in the database (no updated_at bump). Returns the number of rows changed.</summary>
    Task<int> MarkReadAsync(Guid clinicId, Guid conversationId, DateTimeOffset readAt, CancellationToken ct = default);
}
