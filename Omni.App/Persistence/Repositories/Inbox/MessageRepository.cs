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

    public Task<bool> HasMessagesSinceAsync(Guid conversationId, DateTimeOffset since, Guid messageId, CancellationToken ct = default) =>
        _db.Messages.AnyAsync(m => m.ConversationId == conversationId && m.Id != messageId && m.CreatedAt >= since, ct);

    public Task<Message?> GetForClientReadOnlyAsync(Guid clientId, Guid messageId, CancellationToken ct = default) =>
        _db.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.ClientId == clientId && m.Id == messageId, ct);

    public Task<Guid?> GetLatestInboundIdAsync(Guid conversationId, CancellationToken ct = default) =>
        _db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.Direction == MessageDirection.Inbound)
            .OrderByDescending(m => m.CreatedAt).Select(m => (Guid?)m.Id).FirstOrDefaultAsync(ct);

    public async Task<IAsyncDisposable?> LockReplyAsync(Guid messageId, TimeSpan wait, CancellationToken ct = default)
    {
        // A session lock lives on one connection: keep this context's connection open until it is released.
        await _db.Database.OpenConnectionAsync(ct);
        var key = BitConverter.ToInt64(messageId.ToByteArray(), 0);
        var until = DateTimeOffset.UtcNow + wait;
        while (true)
        {
            if (await _db.Database.SqlQuery<bool>($"select pg_try_advisory_lock({key}) as \"Value\"").SingleAsync(ct)) return new ReplyLock(_db, key);
            if (DateTimeOffset.UtcNow >= until)
            {
                await _db.Database.CloseConnectionAsync();
                return null;
            }
            await Task.Delay(100, ct);
        }
    }

    private sealed class ReplyLock(ApplicationDbContext db, long key) : IAsyncDisposable
    {
        private bool _released;

        public async ValueTask DisposeAsync()
        {
            if (_released) return;
            _released = true;
            try
            {
                await db.Database.SqlQuery<bool>($"select pg_advisory_unlock({key}) as \"Value\"").SingleAsync();
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }

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
