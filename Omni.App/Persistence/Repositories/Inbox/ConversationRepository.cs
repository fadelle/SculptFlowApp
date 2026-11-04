using Microsoft.EntityFrameworkCore;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.Inbox;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Inbox;
using Omni.App.Persistence.Helpers;
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

    public async Task<ConversationListPage> ListInboxAsync(Guid clientId, ConversationListRequest request, CancellationToken ct = default)
    {
        var query = _db.Conversations.Where(c => c.ClientId == clientId);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = QueryPaging.Like(request.Search);
            query = query.Where(c => EF.Functions.ILike(c.Contact!.FullName ?? "", pattern) || EF.Functions.ILike(c.Contact.Phone ?? "", pattern)
                                     || (c.Contact.Service != null && EF.Functions.ILike(c.Contact.Service.Name, pattern)));
        }

        // Every tab's count for this search in one query (the tabs and their numbers cover everything, not one page).
        var counts = await query.GroupBy(_ => 1).Select(g => new InboxListCounts(
                g.Count(),
                g.Count(c => c.Status != ConversationStatus.Closed && c.Status != ConversationStatus.Archived
                             && (c.Mode == ConversationMode.Human || c.Mode == ConversationMode.Approval)),
                g.Count(c => c.Status != ConversationStatus.Closed && c.Status != ConversationStatus.Archived && c.Mode == ConversationMode.Ai),
                g.Count(c => c.Status == ConversationStatus.Closed || c.Status == ConversationStatus.Archived)))
            .FirstOrDefaultAsync(ct) ?? new InboxListCounts(0, 0, 0, 0);

        query = request.Filter switch
        {
            InboxListFilter.Closed => query.Where(c => c.Status == ConversationStatus.Closed || c.Status == ConversationStatus.Archived),
            InboxListFilter.Needs => query.Where(c => c.Status != ConversationStatus.Closed && c.Status != ConversationStatus.Archived
                                                      && (c.Mode == ConversationMode.Human || c.Mode == ConversationMode.Approval)),
            InboxListFilter.Ai => query.Where(c => c.Status != ConversationStatus.Closed && c.Status != ConversationStatus.Archived && c.Mode == ConversationMode.Ai),
            _ => query
        };
        var totalCount = request.Filter switch
        {
            InboxListFilter.Closed => counts.Closed,
            InboxListFilter.Needs => counts.Needs,
            InboxListFilter.Ai => counts.Ai,
            _ => counts.All
        };

        // Customer messages nobody has opened yet, in every conversation (not just this page or tab).
        var unreadTotal = await _db.Messages.CountAsync(m => m.ClientId == clientId && m.Direction == MessageDirection.Inbound
            && _db.Conversations.Any(c => c.Id == m.ConversationId && (c.LastReadAt == null || m.CreatedAt > c.LastReadAt)), ct);

        var items = await query
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt).ThenByDescending(c => c.Id)
            .Skip(request.Skip)
            .Take(request.Take)
            .Select(c => new ConversationListRow(
                c.Id, c.ContactId, c.Contact!.FullName, c.Contact.Phone, c.Contact.Service != null ? c.Contact.Service.Name : null,
                c.Channel, c.Status, c.Mode,
                _db.Messages.Where(m => m.ConversationId == c.Id && m.Origin != MessageOrigin.System).OrderByDescending(m => m.CreatedAt).Select(m => m.Content).FirstOrDefault(),
                c.LastMessageDirection, c.LastMessageAt, c.CreatedAt,
                _db.Messages.Count(m => m.ConversationId == c.Id && m.Direction == MessageDirection.Inbound
                                        && (c.LastReadAt == null || m.CreatedAt > c.LastReadAt))))
            .ToListAsync(ct);

        return new ConversationListPage(items, totalCount, counts, unreadTotal);
    }

    public Task<int> MarkReadAsync(Guid clientId, Guid conversationId, DateTimeOffset readAt, CancellationToken ct = default) =>
        _db.Conversations
            .Where(c => c.ClientId == clientId && c.Id == conversationId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastReadAt, readAt), ct);
}
