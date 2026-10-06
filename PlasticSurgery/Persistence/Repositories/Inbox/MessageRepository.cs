using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Inbox;

namespace PlasticSurgery.Persistence.Repositories.Inbox;

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

    public Task<Message?> FindByExternalIdAsync(Guid clinicId, string channel, string externalMessageId, CancellationToken ct = default) =>
        _db.Messages.FirstOrDefaultAsync(m =>
            m.ClinicId == clinicId && m.Channel == channel && m.ExternalMessageId == externalMessageId, ct);

    public Task<string?> GetExternalIdAsync(Guid messageId, CancellationToken ct = default) =>
        _db.Messages.Where(m => m.Id == messageId).Select(m => m.ExternalMessageId).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<(Guid ConversationId, DateTimeOffset CreatedAt)>> ListInboundCustomerMessageTimesAsync(
        Guid clinicId, IReadOnlyCollection<Guid> conversationIds, CancellationToken ct = default)
    {
        var rows = await _db.Messages
            .Where(m => m.ClinicId == clinicId && conversationIds.Contains(m.ConversationId)
                && m.Direction == MessageDirection.Inbound && m.Origin == MessageOrigin.WhatsAppCustomer)
            .Select(m => new { m.ConversationId, m.CreatedAt })
            .ToListAsync(ct);
        return rows.Select(r => (r.ConversationId, r.CreatedAt)).ToList();
    }

    public async Task<IReadOnlyList<Message>> ListByIdsReadOnlyAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default) =>
        await _db.Messages.AsNoTracking().Where(m => messageIds.Contains(m.Id)).ToListAsync(ct);
}
