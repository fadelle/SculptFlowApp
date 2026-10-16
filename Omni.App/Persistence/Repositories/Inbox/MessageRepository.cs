using Microsoft.EntityFrameworkCore;
using Omni.App.Common.Enums;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Inbox;

namespace Omni.App.Persistence.Repositories.Inbox;

public class MessageRepository : IMessageRepository
{
    private readonly ApplicationDbContext _db;

    public MessageRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public void Add(Message message) => _db.Messages.Add(message);

    public async Task<IReadOnlyList<Message>> ListForConversationAsync(Guid conversationId, CancellationToken ct = default) =>
        await _db.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

    public Task<Message?> FindByExternalIdAsync(Guid clientId, string channel, string externalMessageId, CancellationToken ct = default) =>
        _db.Messages.FirstOrDefaultAsync(m =>
            m.ClientId == clientId && m.Channel == channel && m.ExternalMessageId == externalMessageId, ct);

    public Task<Message?> GetForClientReadOnlyAsync(Guid clientId, Guid messageId, CancellationToken ct = default) =>
        _db.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.ClientId == clientId && m.Id == messageId, ct);

    public Task<string?> GetExternalIdAsync(Guid messageId, CancellationToken ct = default) =>
        _db.Messages.Where(m => m.Id == messageId).Select(m => m.ExternalMessageId).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<(Guid ConversationId, DateTimeOffset CreatedAt)>> ListInboundCustomerMessageTimesAsync(
        Guid clientId, IReadOnlyCollection<Guid> conversationIds, CancellationToken ct = default)
    {
        var rows = await _db.Messages
            .Where(m => m.ClientId == clientId && conversationIds.Contains(m.ConversationId)
                && m.Direction == MessageDirection.Inbound && m.Origin == MessageOrigin.WhatsAppCustomer)
            .Select(m => new { m.ConversationId, m.CreatedAt })
            .ToListAsync(ct);
        return rows.Select(r => (r.ConversationId, r.CreatedAt)).ToList();
    }

    public async Task<IReadOnlyList<Message>> ListByIdsReadOnlyAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default) =>
        await _db.Messages.AsNoTracking().Where(m => messageIds.Contains(m.Id)).ToListAsync(ct);
}
