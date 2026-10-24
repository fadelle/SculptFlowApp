using Microsoft.EntityFrameworkCore;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Inbox;

namespace Omni.App.Persistence.Repositories.Inbox;

// Not cached: read only for a contact's first message in a conversation or one that reopens it, and on the settings page.
public class AutoReplyRepository : IAutoReplyRepository
{
    private readonly ApplicationDbContext _db;

    public AutoReplyRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<AutoReply>> ListForClientReadOnlyAsync(Guid clientId, CancellationToken ct = default) =>
        await _db.AutoReplies.AsNoTracking().Where(a => a.ClientId == clientId).OrderBy(a => a.Channel).ToListAsync(ct);

    public Task<AutoReply?> GetReadOnlyAsync(Guid clientId, string channel, CancellationToken ct = default) =>
        _db.AutoReplies.AsNoTracking().FirstOrDefaultAsync(a => a.ClientId == clientId && a.Channel == channel, ct);

    public Task<AutoReply?> GetAsync(Guid clientId, string channel, CancellationToken ct = default) =>
        _db.AutoReplies.FirstOrDefaultAsync(a => a.ClientId == clientId && a.Channel == channel, ct);

    public void Add(AutoReply autoReply) => _db.AutoReplies.Add(autoReply);
}
