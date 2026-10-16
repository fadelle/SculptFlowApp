using Microsoft.EntityFrameworkCore;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Billing;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.PlatformAdmin;
using Omni.App.Persistence.Helpers;

namespace Omni.App.Persistence.Repositories.PlatformAdmin;

public class ContentAdminRepository : IContentAdminRepository
{
    private readonly ApplicationDbContext _db;

    public ContentAdminRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<PagedResponse<CampaignRow>> ListCampaignsAsync(Guid? clientId, string? status, int page, CancellationToken ct = default)
    {
        var q = _db.Campaigns.AsNoTracking().AsQueryable();
        if (clientId.HasValue) q = q.Where(c => c.ClientId == clientId.Value);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(c => c.Status == status);
        return ToRows(q.OrderByDescending(c => c.CreatedAt)).ToPagedAsync(page, ct: ct);
    }

    public Task<CampaignRow?> GetCampaignAsync(Guid id, CancellationToken ct = default) =>
        ToRows(_db.Campaigns.AsNoTracking().Where(c => c.Id == id)).FirstOrDefaultAsync(ct);

    public static IQueryable<CampaignRow> ToRows(IQueryable<Campaign> q) =>
        q.Select(c => new CampaignRow(c.Id, c.ClientId, c.Client!.Name, c.Name, c.CampaignType, c.Channel, c.Status,
                c.WhatsAppTemplate != null ? c.WhatsAppTemplate.Name : null,
                c.Recipients.Count(),
                c.Recipients.Count(r => r.SentAt != null),
                c.Recipients.Count(r => r.DeliveredAt != null),
                c.Recipients.Count(r => r.ReadAt != null),
                c.Recipients.Count(r => r.RepliedAt != null),
                c.Recipients.Count(r => r.BookedAt != null),
                c.Recipients.Count(r => r.Status == CampaignRecipientStatus.Failed),
                c.Recipients.Count(r => r.Status == CampaignRecipientStatus.Skipped),
                c.ScheduledAt, c.StartedAt, c.CompletedAt, c.CreatedAt));

    public Task<PagedResponse<RecipientRow>> RecipientsAsync(Guid campaignId, string? status, int page, CancellationToken ct = default)
    {
        var q = _db.CampaignRecipients.AsNoTracking().Where(r => r.CampaignId == campaignId);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(r => r.Status == status);
        return q.OrderBy(r => r.CreatedAt)
            .Select(r => new RecipientRow(r.Id, r.LeadId,
                r.Lead!.FullName ?? ((r.Lead.FirstName ?? "") + " " + (r.Lead.LastName ?? "")).Trim(),
                r.PhoneNumber, r.Status, r.SkipReason, r.FailureReason, r.SentAt, r.RepliedAt, r.BookedAt))
            .ToPagedAsync(page, 100, ct);
    }

    public Task<List<TemplateRow>> ListTemplatesAsync(Guid? clientId, string? status, CancellationToken ct = default)
    {
        var q = _db.WhatsAppTemplates.AsNoTracking().AsQueryable();
        if (clientId.HasValue) q = q.Where(t => t.ClientId == clientId.Value);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(t => t.Status == status);
        return q.OrderBy(t => t.Client!.Name).ThenBy(t => t.Name)
            .Select(t => new TemplateRow(t.Id, t.ClientId, t.Client!.Name, t.Name, t.Category, t.Language, t.Status,
                t.QualityRating, t.RejectionReason, t.Body, t.UpdatedAt))
            .Take(1000).ToListAsync(ct);
    }

    public Task<List<ProcedureRow>> ListProceduresAsync(Guid? clientId, CancellationToken ct = default)
    {
        var q = _db.Procedures.AsNoTracking().AsQueryable();
        if (clientId.HasValue) q = q.Where(p => p.ClientId == clientId.Value);
        return q.OrderBy(p => p.Client!.Name).ThenBy(p => p.Name)
            .Select(p => new ProcedureRow(p.Id, p.ClientId, p.Client!.Name, p.Name, p.Code, p.ConsultationDuration, p.IsActive,
                _db.Leads.Count(l => l.ProcedureId == p.Id), p.UpdatedAt))
            .Take(2000).ToListAsync(ct);
    }

    public Task<PagedResponse<KnowledgeDocRow>> ListDocumentsAsync(Guid? clientId, string? search, bool? active, int page,
        CancellationToken ct = default)
    {
        var q = _db.KnowledgeDocuments.AsNoTracking().AsQueryable();
        if (clientId.HasValue) q = q.Where(d => d.ClientId == clientId.Value);
        if (active.HasValue) q = q.Where(d => d.IsActive == active.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = QueryPaging.Like(search);
            q = q.Where(d => EF.Functions.ILike(d.Title, like) || EF.Functions.ILike(d.Content, like));
        }
        return q.OrderByDescending(d => d.UpdatedAt)
            .Select(d => new KnowledgeDocRow(d.Id, d.ClientId, d.Client!.Name, d.Title, d.Category, d.SourceType, d.SourceUrl,
                d.IsActive, d.Chunks.Count(), d.Content.Length, d.UpdatedAt))
            .ToPagedAsync(page, ct: ct);
    }

    public Task<KnowledgeDocument?> GetDocumentAsync(Guid id, CancellationToken ct = default) =>
        _db.KnowledgeDocuments.AsNoTracking().Include(d => d.Client).FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<KnowledgeSearchSettings?> SearchSettingsAsync(Guid clientId, CancellationToken ct = default) =>
        _db.KnowledgeSearchSettings.AsNoTracking().FirstOrDefaultAsync(s => s.ClientId == clientId, ct);

    public Task<List<WebsiteSourceRow>> ListWebsitesAsync(Guid? clientId, CancellationToken ct = default)
    {
        var q = _db.KnowledgeWebsiteSources.AsNoTracking().AsQueryable();
        if (clientId.HasValue) q = q.Where(w => w.ClientId == clientId.Value);
        return q.OrderByDescending(w => w.UpdatedAt)
            .Select(w => new WebsiteSourceRow(w.Id, w.ClientId,
                _db.Clients.Where(x => x.Id == w.ClientId).Select(x => x.Name).FirstOrDefault() ?? "",
                w.StartUrl, w.CrawlMode, w.IsActive, w.Status,
                _db.KnowledgeWebsitePages.Count(p => p.WebsiteSourceId == w.Id && p.Status != WebsitePageStatus.Removed),
                w.LastScrapedAt,
                _db.KnowledgeWebsiteScrapeRuns.Where(r => r.WebsiteSourceId == w.Id).OrderByDescending(r => r.CreatedAt)
                    .Select(r => r.ErrorSummary).FirstOrDefault()))
            .ToListAsync(ct);
    }

    public Task<Procedure?> GetProcedureAsync(Guid id, CancellationToken ct = default) =>
        _db.Procedures.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
}
