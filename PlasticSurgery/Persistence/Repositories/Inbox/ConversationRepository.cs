using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.Inbox;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Inbox;

namespace PlasticSurgery.Persistence.Repositories.Inbox;

public class ConversationRepository : IConversationRepository
{
    private readonly ApplicationDbContext _db;

    public ConversationRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public void Add(Conversation conversation) => _db.Conversations.Add(conversation);

    public Task<Conversation?> GetAsync(Guid clinicId, Guid conversationId, CancellationToken ct = default) =>
        _db.Conversations.FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.Id == conversationId, ct);

    public Task<Conversation?> GetWithLeadAsync(Guid clinicId, Guid conversationId, CancellationToken ct = default) =>
        _db.Conversations.Include(c => c.Lead).FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.Id == conversationId, ct);

    public Task<Conversation?> FindForLeadAsync(Guid clinicId, Guid leadId, string channel, CancellationToken ct = default) =>
        _db.Conversations.FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.LeadId == leadId && c.Channel == channel, ct);

    public Task<Conversation?> FindByExternalThreadAsync(Guid clinicId, string channel, string externalThreadId,
        CancellationToken ct = default) =>
        _db.Conversations.FirstOrDefaultAsync(
            c => c.ClinicId == clinicId && c.Channel == channel && c.ExternalThreadId == externalThreadId, ct);

    public Task<bool> ExistsAsync(Guid clinicId, Guid conversationId, CancellationToken ct = default) =>
        _db.Conversations.AnyAsync(c => c.ClinicId == clinicId && c.Id == conversationId, ct);

    public Task<bool> IsInHumanModeAsync(Guid clinicId, Guid conversationId, CancellationToken ct = default) =>
        _db.Conversations
            .Where(c => c.ClinicId == clinicId && c.Id == conversationId)
            .Select(c => c.Mode == ConversationMode.Human)
            .FirstOrDefaultAsync(ct);

    public async Task<(IReadOnlyList<ConversationListRow> Items, int TotalCount)> ListInboxAsync(Guid clinicId, int skip, int take,
        CancellationToken ct = default)
    {
        var query = _db.Conversations
            .Include(c => c.Lead).ThenInclude(l => l!.Procedure)
            .Where(c => c.ClinicId == clinicId);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .Skip(skip)
            .Take(take)
            .Select(c => new ConversationListRow(
                c.Id, c.LeadId, c.Lead!.FullName, c.Lead.Phone, c.Lead.Procedure != null ? c.Lead.Procedure.Name : null,
                c.Channel, c.Status, c.Mode,
                _db.Messages.Where(m => m.ConversationId == c.Id && m.Origin != MessageOrigin.System).OrderByDescending(m => m.CreatedAt).Select(m => m.Content).FirstOrDefault(),
                c.LastMessageDirection, c.LastMessageAt, c.CreatedAt,
                _db.Messages.Count(m => m.ConversationId == c.Id && m.Direction == MessageDirection.Inbound
                                        && (c.LastReadAt == null || m.CreatedAt > c.LastReadAt))))
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public Task<int> MarkReadAsync(Guid clinicId, Guid conversationId, DateTimeOffset readAt, CancellationToken ct = default) =>
        _db.Conversations
            .Where(c => c.ClinicId == clinicId && c.Id == conversationId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastReadAt, readAt), ct);
}
