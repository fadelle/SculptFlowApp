using Microsoft.EntityFrameworkCore;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.Inbox;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Inbox;

namespace Omni.App.Persistence.Repositories.Inbox;

public class ConversationRepository : IConversationRepository
{
    private readonly ApplicationDbContext _db;

    public ConversationRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public void Add(Conversation conversation) => _db.Conversations.Add(conversation);

    public Task<Conversation?> GetAsync(Guid clientId, Guid conversationId, CancellationToken ct = default) =>
        _db.Conversations.FirstOrDefaultAsync(c => c.ClientId == clientId && c.Id == conversationId, ct);

    public Task<Conversation?> GetForUpdateAsync(Guid clientId, Guid conversationId, CancellationToken ct = default) =>
        _db.Conversations
            .FromSqlInterpolated($"select * from crm.conversations where id = {conversationId} and client_id = {clientId} for update")
            .FirstOrDefaultAsync(ct);

    public Task<Conversation?> GetWithContactAsync(Guid clientId, Guid conversationId, CancellationToken ct = default) =>
        _db.Conversations.Include(c => c.Contact).FirstOrDefaultAsync(c => c.ClientId == clientId && c.Id == conversationId, ct);

    public Task<Conversation?> FindForContactAsync(Guid clientId, Guid contactId, string channel, CancellationToken ct = default) =>
        _db.Conversations.FirstOrDefaultAsync(c => c.ClientId == clientId && c.ContactId == contactId && c.Channel == channel, ct);

    public Task<Conversation?> FindByExternalThreadAsync(Guid clientId, string channel, string externalThreadId,
        CancellationToken ct = default) =>
        _db.Conversations.FirstOrDefaultAsync(
            c => c.ClientId == clientId && c.Channel == channel && c.ExternalThreadId == externalThreadId, ct);

    public Task<bool> ExistsAsync(Guid clientId, Guid conversationId, CancellationToken ct = default) =>
        _db.Conversations.AnyAsync(c => c.ClientId == clientId && c.Id == conversationId, ct);

    public Task<bool> IsInHumanModeAsync(Guid clientId, Guid conversationId, CancellationToken ct = default) =>
        _db.Conversations
            .Where(c => c.ClientId == clientId && c.Id == conversationId)
            .Select(c => c.Mode == ConversationMode.Human)
            .FirstOrDefaultAsync(ct);

    public async Task<(IReadOnlyList<ConversationListRow> Items, int TotalCount)> ListInboxAsync(Guid clientId, int skip, int take,
        CancellationToken ct = default)
    {
        var query = _db.Conversations
            .Include(c => c.Contact).ThenInclude(l => l!.Service)
            .Where(c => c.ClientId == clientId);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .Skip(skip)
            .Take(take)
            .Select(c => new ConversationListRow(
                c.Id, c.ContactId, c.Contact!.FullName, c.Contact.Phone, c.Contact.Service != null ? c.Contact.Service.Name : null,
                c.Channel, c.Status, c.Mode,
                _db.Messages.Where(m => m.ConversationId == c.Id && m.Origin != MessageOrigin.System).OrderByDescending(m => m.CreatedAt).Select(m => m.Content).FirstOrDefault(),
                c.LastMessageDirection, c.LastMessageAt, c.CreatedAt,
                _db.Messages.Count(m => m.ConversationId == c.Id && m.Direction == MessageDirection.Inbound
                                        && (c.LastReadAt == null || m.CreatedAt > c.LastReadAt))))
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public Task<int> MarkReadAsync(Guid clientId, Guid conversationId, DateTimeOffset readAt, CancellationToken ct = default) =>
        _db.Conversations
            .Where(c => c.ClientId == clientId && c.Id == conversationId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastReadAt, readAt), ct);
}
