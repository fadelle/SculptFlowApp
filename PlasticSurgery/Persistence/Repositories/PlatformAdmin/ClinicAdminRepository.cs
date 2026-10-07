using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.PlatformAdmin;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.Billing;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.PlatformAdmin;
using PlasticSurgery.Persistence.Helpers;

namespace PlasticSurgery.Persistence.Repositories.PlatformAdmin;

public class ClinicAdminRepository : IClinicAdminRepository
{
    private readonly ApplicationDbContext _db;

    public ClinicAdminRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResponse<ClinicRow>> ListAsync(string? search, bool? active, int page, CancellationToken ct = default)
    {
        var q = _db.Clinics.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = QueryPaging.Like(search);
            q = q.Where(c => EF.Functions.ILike(c.Name, like) || EF.Functions.ILike(c.Slug, like)
                             || (c.Email != null && EF.Functions.ILike(c.Email, like)));
        }
        if (active.HasValue) q = q.Where(c => c.IsActive == active.Value);

        return await q.OrderByDescending(c => c.CreatedAt)
            .Select(c => new ClinicRow(c.Id, c.Name, c.Slug, c.Email, c.Timezone, c.IsActive,
                _db.ClinicUsers.Count(u => u.ClinicId == c.Id && u.IsActive),
                _db.Leads.Count(l => l.ClinicId == c.Id),
                _db.Conversations.Count(x => x.ClinicId == c.Id),
                _db.Conversations.Where(x => x.ClinicId == c.Id).Max(x => x.LastMessageAt),
                _db.ChannelIntegrations.Where(i => i.ClinicId == c.Id && i.Channel == ChannelType.WhatsApp)
                    .Select(i => i.Status).FirstOrDefault() ?? "not set up",
                c.CreatedAt))
            .ToPagedAsync(page, ct: ct);
    }

    /// <summary>Every clinic as (id, name), for the clinic filter on list pages.</summary>
    public async Task<List<ClinicOption>> OptionsAsync(CancellationToken ct = default) =>
        await _db.Clinics.AsNoTracking().OrderBy(c => c.Name).Select(c => new ClinicOption(c.Id, c.Name)).ToListAsync(ct);

    public Task<Clinic?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.Clinics.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<ClinicCounts> CountsAsync(Guid id, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var weekAgo = now.AddDays(-7);
        return new ClinicCounts(
            await _db.ClinicUsers.CountAsync(u => u.ClinicId == id && u.IsActive, ct),
            await _db.Leads.CountAsync(l => l.ClinicId == id, ct),
            await _db.Conversations.CountAsync(c => c.ClinicId == id, ct),
            await _db.Messages.CountAsync(m => m.ClinicId == id, ct),
            await _db.Messages.CountAsync(m => m.ClinicId == id && m.CreatedAt >= weekAgo, ct),
            await _db.Messages.CountAsync(m => m.ClinicId == id && m.CreatedAt >= weekAgo && m.SenderType == MessageSenderType.Ai, ct),
            await _db.Messages.CountAsync(m => m.ClinicId == id && m.CreatedAt >= weekAgo && m.FailedAt != null, ct),
            await _db.Appointments.CountAsync(a => a.ClinicId == id && a.ScheduledStart >= now
                && a.Status != AppointmentStatus.Canceled && a.Status != AppointmentStatus.Rescheduled, ct),
            await _db.Procedures.CountAsync(p => p.ClinicId == id, ct),
            await _db.Procedures.CountAsync(p => p.ClinicId == id && p.IsActive, ct),
            await _db.KnowledgeDocuments.CountAsync(d => d.ClinicId == id, ct),
            await _db.Campaigns.CountAsync(c => c.ClinicId == id, ct),
            await _db.Campaigns.CountAsync(c => c.ClinicId == id && c.Status == CampaignStatus.Running, ct),
            await _db.WhatsAppTemplates.CountAsync(t => t.ClinicId == id, ct),
            await _db.Notifications.CountAsync(n => n.ClinicId == id && !n.IsRead, ct));
    }

    /// <summary>Tracked.</summary>
    public Task<Clinic?> GetForUpdateAsync(Guid id, CancellationToken ct = default) =>
        _db.Clinics.FirstOrDefaultAsync(c => c.Id == id, ct);
}
