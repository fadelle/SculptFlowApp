using Microsoft.EntityFrameworkCore;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Billing;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.PlatformAdmin;
using Omni.App.Persistence.Helpers;

namespace Omni.App.Persistence.Repositories.PlatformAdmin;

public class ClientAdminRepository : IClientAdminRepository
{
    private readonly ApplicationDbContext _db;

    public ClientAdminRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResponse<ClientRow>> ListAsync(string? search, bool? active, int page, CancellationToken ct = default)
    {
        var q = _db.Clients.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = QueryPaging.Like(search);
            q = q.Where(c => EF.Functions.ILike(c.Name, like) || EF.Functions.ILike(c.Slug, like)
                             || (c.Email != null && EF.Functions.ILike(c.Email, like)));
        }
        if (active.HasValue) q = q.Where(c => c.IsActive == active.Value);

        return await q.OrderByDescending(c => c.CreatedAt)
            .Select(c => new ClientRow(c.Id, c.Name, c.Slug, c.Email, c.Timezone, c.IsActive,
                _db.ClientUsers.Count(u => u.ClientId == c.Id && u.IsActive),
                _db.Leads.Count(l => l.ClientId == c.Id),
                _db.Conversations.Count(x => x.ClientId == c.Id),
                _db.Conversations.Where(x => x.ClientId == c.Id).Max(x => x.LastMessageAt),
                _db.ChannelIntegrations.Where(i => i.ClientId == c.Id && i.Channel == ChannelType.WhatsApp)
                    .Select(i => i.Status).FirstOrDefault() ?? "not set up",
                c.CreatedAt))
            .ToPagedAsync(page, ct: ct);
    }

    /// <summary>Every client as (id, name), for the client filter on list pages.</summary>
    public async Task<List<ClientOption>> OptionsAsync(CancellationToken ct = default) =>
        await _db.Clients.AsNoTracking().OrderBy(c => c.Name).Select(c => new ClientOption(c.Id, c.Name)).ToListAsync(ct);

    public Task<Client?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<ClientCounts> CountsAsync(Guid id, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var weekAgo = now.AddDays(-7);
        return new ClientCounts(
            await _db.ClientUsers.CountAsync(u => u.ClientId == id && u.IsActive, ct),
            await _db.Leads.CountAsync(l => l.ClientId == id, ct),
            await _db.Conversations.CountAsync(c => c.ClientId == id, ct),
            await _db.Messages.CountAsync(m => m.ClientId == id, ct),
            await _db.Messages.CountAsync(m => m.ClientId == id && m.CreatedAt >= weekAgo, ct),
            await _db.Messages.CountAsync(m => m.ClientId == id && m.CreatedAt >= weekAgo && m.SenderType == MessageSenderType.Ai, ct),
            await _db.Messages.CountAsync(m => m.ClientId == id && m.CreatedAt >= weekAgo && m.FailedAt != null, ct),
            await _db.Appointments.CountAsync(a => a.ClientId == id && a.ScheduledStart >= now
                && a.Status != AppointmentStatus.Canceled && a.Status != AppointmentStatus.Rescheduled, ct),
            await _db.Procedures.CountAsync(p => p.ClientId == id, ct),
            await _db.Procedures.CountAsync(p => p.ClientId == id && p.IsActive, ct),
            await _db.KnowledgeDocuments.CountAsync(d => d.ClientId == id, ct),
            await _db.Campaigns.CountAsync(c => c.ClientId == id, ct),
            await _db.Campaigns.CountAsync(c => c.ClientId == id && c.Status == CampaignStatus.Running, ct),
            await _db.WhatsAppTemplates.CountAsync(t => t.ClientId == id, ct),
            await _db.Notifications.CountAsync(n => n.ClientId == id && !n.IsRead, ct));
    }

    /// <summary>Tracked.</summary>
    public Task<Client?> GetForUpdateAsync(Guid id, CancellationToken ct = default) =>
        _db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct);
}
