using Microsoft.EntityFrameworkCore;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Billing;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.PlatformAdmin;
using Omni.App.Persistence.Helpers;

namespace Omni.App.Persistence.Repositories.PlatformAdmin;

public class OverviewAdminRepository : IOverviewAdminRepository
{
    private readonly ApplicationDbContext _db;

    public OverviewAdminRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<SystemTotals> TotalsAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var day = now.AddDays(-1);
        var week = now.AddDays(-7);
        return new SystemTotals(
            await _db.Clients.CountAsync(ct),
            await _db.Clients.CountAsync(c => c.IsActive, ct),
            await _db.ClientUsers.CountAsync(u => u.IsActive, ct),
            await _db.Leads.CountAsync(ct),
            await _db.Leads.CountAsync(l => l.CreatedAt >= week, ct),
            await _db.Conversations.CountAsync(ct),
            await _db.Conversations.CountAsync(c => c.Mode == ConversationMode.Human && c.Status == ConversationStatus.Active, ct),
            await _db.Messages.CountAsync(m => m.CreatedAt >= day, ct),
            await _db.Messages.CountAsync(m => m.CreatedAt >= week, ct),
            await _db.Messages.CountAsync(m => m.CreatedAt >= week && m.SenderType == MessageSenderType.Ai, ct),
            await _db.Messages.CountAsync(m => m.CreatedAt >= week && m.Direction == MessageDirection.Inbound, ct),
            await _db.Messages.CountAsync(m => m.CreatedAt >= week && m.FailedAt != null, ct),
            await _db.Appointments.CountAsync(a => a.ScheduledStart >= now && a.Status != AppointmentStatus.Canceled
                                                   && a.Status != AppointmentStatus.Rescheduled, ct),
            await _db.Appointments.CountAsync(a => a.CreatedAt >= week, ct),
            await _db.Campaigns.CountAsync(c => c.Status == CampaignStatus.Running, ct),
            await _db.ChannelIntegrations.CountAsync(c => c.Status == ChannelIntegrationStatus.Connected, ct),
            await _db.ChannelIntegrations.CountAsync(c => c.Status == ChannelIntegrationStatus.Error
                || (c.Status == ChannelIntegrationStatus.Connected && !c.IsHealthy), ct),
            await _db.CalendarIntegrations.CountAsync(c => c.Status == CalendarIntegrationStatus.Error
                || (c.Status == CalendarIntegrationStatus.Connected && !c.IsHealthy), ct),
            await _db.KnowledgeDocuments.CountAsync(ct));
    }

    /// <summary>Messages per UTC day for the last <paramref name="days"/> days, split by who sent them.</summary>
    public async Task<List<DailyCount>> DailyMessagesAsync(int days = 14, CancellationToken ct = default)
    {
        var from = DateTimeOffset.UtcNow.Date.AddDays(-(days - 1));
        var fromUtc = new DateTimeOffset(from, TimeSpan.Zero);
        // Only the few columns needed are read and bucketed here; a timestamptz → UTC date GROUP BY doesn't translate.
        var rows = await _db.Messages.AsNoTracking().Where(m => m.CreatedAt >= fromUtc)
            .Select(m => new { m.CreatedAt, m.Direction, m.SenderType, Failed = m.FailedAt != null })
            .ToListAsync(ct);
        var byDay = rows.ToLookup(m => m.CreatedAt.UtcDateTime.Date);
        return Enumerable.Range(0, days).Select(i =>
        {
            var d = from.AddDays(i);
            var g = byDay[d];
            return new DailyCount(DateOnly.FromDateTime(d), g.Count(m => m.Direction == MessageDirection.Inbound),
                g.Count(m => m.SenderType == MessageSenderType.Ai), g.Count(m => m.SenderType == MessageSenderType.Staff),
                g.Count(m => m.Failed));
        }).ToList();
    }

    /// <summary>Everything that probably needs an admin: broken channels and calendars, failing sends, stuck campaigns.</summary>
    public async Task<List<ProblemRow>> ProblemsAsync(CancellationToken ct = default)
    {
        var problems = new List<ProblemRow>();
        var day = DateTimeOffset.UtcNow.AddDays(-1);

        problems.AddRange(await _db.ChannelIntegrations.AsNoTracking()
            .Where(c => c.Status == ChannelIntegrationStatus.Error || (c.Status == ChannelIntegrationStatus.Connected
                        && (!c.IsHealthy || c.LastError != null || c.WebhookStatus == WebhookStatus.Error)))
            .Select(c => new ProblemRow("Channel", c.ClientId, c.Client!.Name, c.Channel + " " + (c.HealthLevel ?? c.Status),
                c.LastProblemMessage ?? c.LastError, c.LastHealthEventAt ?? c.UpdatedAt, "/Channels/Details?id=" + c.Id))
            .ToListAsync(ct));

        problems.AddRange(await _db.CalendarIntegrations.AsNoTracking()
            .Where(c => c.Status == CalendarIntegrationStatus.Error || (c.Status == CalendarIntegrationStatus.Connected && !c.IsHealthy))
            .Select(c => new ProblemRow("Calendar", c.ClientId,
                _db.Clients.Where(x => x.Id == c.ClientId).Select(x => x.Name).FirstOrDefault() ?? "",
                c.Provider + " calendar " + c.Status, c.LastProblemMessage, c.UpdatedAt, "/Channels?tab=calendars"))
            .ToListAsync(ct));

        problems.AddRange(await _db.TikTokIntegrations.AsNoTracking()
            .Where(c => c.Status == TikTokIntegrationStatus.Error || (c.Status == TikTokIntegrationStatus.Connected && !c.IsHealthy))
            .Select(c => new ProblemRow("TikTok", c.ClientId,
                _db.Clients.Where(x => x.Id == c.ClientId).Select(x => x.Name).FirstOrDefault() ?? "",
                "TikTok " + c.Status, c.LastProblemMessage, c.UpdatedAt, "/Channels?tab=tiktok"))
            .ToListAsync(ct));

        var failing = await _db.Messages.AsNoTracking().Where(m => m.FailedAt != null && m.CreatedAt >= day)
            .GroupBy(m => m.ClientId)
            .Select(g => new { ClientId = g.Key, Count = g.Count(), Last = g.Max(m => m.CreatedAt),
                Reason = g.OrderByDescending(m => m.CreatedAt).Select(m => m.FailureReason).FirstOrDefault() })
            .ToListAsync(ct);
        var names = await _db.Clients.AsNoTracking().Where(c => failing.Select(f => f.ClientId).Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        problems.AddRange(failing.Select(f => new ProblemRow("Messages", f.ClientId, names.GetValueOrDefault(f.ClientId, ""),
            $"{f.Count} failed message(s) in the last 24h", f.Reason, f.Last, "/Messages?failed=true&clientId=" + f.ClientId)));

        problems.AddRange(await _db.Campaigns.AsNoTracking()
            .Where(c => c.Status == CampaignStatus.Failed
                        || (c.Status == CampaignStatus.Running && c.StartedAt < day))
            .Select(c => new ProblemRow("Campaign", c.ClientId, c.Client!.Name,
                c.Status == CampaignStatus.Failed ? "Campaign failed: " + c.Name : "Campaign running for over a day: " + c.Name,
                null, c.UpdatedAt, "/Campaigns/Details?id=" + c.Id))
            .ToListAsync(ct));

        problems.AddRange(await _db.KnowledgeWebsiteSources.AsNoTracking()
            .Where(w => w.IsActive && w.Status == WebsiteScrapeStatus.Failed)
            .Select(w => new ProblemRow("Website", w.ClientId,
                _db.Clients.Where(x => x.Id == w.ClientId).Select(x => x.Name).FirstOrDefault() ?? "",
                "Website scrape failed: " + w.Host, null, w.LastScrapedAt ?? w.UpdatedAt, "/Knowledge?tab=websites"))
            .ToListAsync(ct));

        return problems.OrderByDescending(p => p.At).ToList();
    }

    public Task<List<ClientRow>> RecentClientsAsync(int take, CancellationToken ct = default) =>
        _db.Clients.AsNoTracking().OrderByDescending(c => c.CreatedAt).Take(take)
            .Select(c => new ClientRow(c.Id, c.Name, c.Slug, c.Email, c.Timezone, c.IsActive,
                _db.ClientUsers.Count(u => u.ClientId == c.Id && u.IsActive),
                _db.Leads.Count(l => l.ClientId == c.Id),
                _db.Conversations.Count(x => x.ClientId == c.Id),
                _db.Conversations.Where(x => x.ClientId == c.Id).Max(x => x.LastMessageAt),
                _db.ChannelIntegrations.Where(i => i.ClientId == c.Id && i.Channel == ChannelType.WhatsApp)
                    .Select(i => i.Status).FirstOrDefault() ?? "not set up",
                c.CreatedAt))
            .ToListAsync(ct);

    public Task<PagedResponse<EventRow>> EventsAsync(Guid? clientId, string? eventType, int page, CancellationToken ct = default)
    {
        var q = _db.Events.AsNoTracking().AsQueryable();
        if (clientId.HasValue) q = q.Where(e => e.ClientId == clientId.Value);
        if (!string.IsNullOrWhiteSpace(eventType)) q = q.Where(e => e.EventType == eventType);
        return q.OrderByDescending(e => e.CreatedAt)
            .Select(e => new EventRow(e.Id, e.ClientId,
                _db.Clients.Where(x => x.Id == e.ClientId).Select(x => x.Name).FirstOrDefault() ?? "",
                e.EventType, e.Source, e.LeadId, e.ConversationId, e.AppointmentId, e.Metadata, e.CreatedAt))
            .ToPagedAsync(page, ct: ct);
    }

    public Task<List<string>> EventTypesAsync(CancellationToken ct = default) =>
        _db.Events.AsNoTracking().Select(e => e.EventType).Distinct().OrderBy(t => t).ToListAsync(ct);

}
