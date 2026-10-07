using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.PlatformAdmin;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.Billing;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.PlatformAdmin;
using PlasticSurgery.Persistence.Helpers;

namespace PlasticSurgery.Persistence.Repositories.PlatformAdmin;

public class ContentAdminRepository : IContentAdminRepository
{
    private readonly ApplicationDbContext _db;

    public ContentAdminRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<PagedResponse<CampaignRow>> ListCampaignsAsync(Guid? clinicId, string? status, int page, CancellationToken ct = default)
    {
        var q = _db.Campaigns.AsNoTracking().AsQueryable();
        if (clinicId.HasValue) q = q.Where(c => c.ClinicId == clinicId.Value);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(c => c.Status == status);
        return ToRows(q.OrderByDescending(c => c.CreatedAt)).ToPagedAsync(page, ct: ct);
    }

    public Task<CampaignRow?> GetCampaignAsync(Guid id, CancellationToken ct = default) =>
        ToRows(_db.Campaigns.AsNoTracking().Where(c => c.Id == id)).FirstOrDefaultAsync(ct);

    public static IQueryable<CampaignRow> ToRows(IQueryable<Campaign> q) =>
        q.Select(c => new CampaignRow(c.Id, c.ClinicId, c.Clinic!.Name, c.Name, c.CampaignType, c.Channel, c.Status,
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

    public Task<List<TemplateRow>> ListTemplatesAsync(Guid? clinicId, string? status, CancellationToken ct = default)
    {
        var q = _db.WhatsAppTemplates.AsNoTracking().AsQueryable();
        if (clinicId.HasValue) q = q.Where(t => t.ClinicId == clinicId.Value);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(t => t.Status == status);
        return q.OrderBy(t => t.Clinic!.Name).ThenBy(t => t.Name)
            .Select(t => new TemplateRow(t.Id, t.ClinicId, t.Clinic!.Name, t.Name, t.Provider ?? "meta", t.Category, t.Language, t.Status,
                t.QualityRating, t.RejectionReason, t.Body, t.UpdatedAt))
            .Take(1000).ToListAsync(ct);
    }

    public Task<List<ProcedureRow>> ListProceduresAsync(Guid? clinicId, CancellationToken ct = default)
    {
        var q = _db.Procedures.AsNoTracking().AsQueryable();
        if (clinicId.HasValue) q = q.Where(p => p.ClinicId == clinicId.Value);
        return q.OrderBy(p => p.Clinic!.Name).ThenBy(p => p.Name)
            .Select(p => new ProcedureRow(p.Id, p.ClinicId, p.Clinic!.Name, p.Name, p.Code, p.ConsultationDuration, p.IsActive,
                _db.Leads.Count(l => l.ProcedureId == p.Id), p.UpdatedAt))
            .Take(2000).ToListAsync(ct);
    }

    public Task<PagedResponse<KnowledgeDocRow>> ListDocumentsAsync(Guid? clinicId, string? search, bool? active, int page,
        CancellationToken ct = default)
    {
        var q = _db.KnowledgeDocuments.AsNoTracking().AsQueryable();
        if (clinicId.HasValue) q = q.Where(d => d.ClinicId == clinicId.Value);
        if (active.HasValue) q = q.Where(d => d.IsActive == active.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = QueryPaging.Like(search);
            q = q.Where(d => EF.Functions.ILike(d.Title, like) || EF.Functions.ILike(d.Content, like));
        }
        return q.OrderByDescending(d => d.UpdatedAt)
            .Select(d => new KnowledgeDocRow(d.Id, d.ClinicId, d.Clinic!.Name, d.Title, d.Category, d.SourceType, d.SourceUrl,
                d.IsActive, d.Chunks.Count(), d.Content.Length, d.UpdatedAt))
            .ToPagedAsync(page, ct: ct);
    }

    public Task<KnowledgeDocument?> GetDocumentAsync(Guid id, CancellationToken ct = default) =>
        _db.KnowledgeDocuments.AsNoTracking().Include(d => d.Clinic).FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<KnowledgeSearchSettings?> SearchSettingsAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.KnowledgeSearchSettings.AsNoTracking().FirstOrDefaultAsync(s => s.ClinicId == clinicId, ct);

    public Task<List<WebsiteSourceRow>> ListWebsitesAsync(Guid? clinicId, CancellationToken ct = default)
    {
        var q = _db.KnowledgeWebsiteSources.AsNoTracking().AsQueryable();
        if (clinicId.HasValue) q = q.Where(w => w.ClinicId == clinicId.Value);
        return q.OrderByDescending(w => w.UpdatedAt)
            .Select(w => new WebsiteSourceRow(w.Id, w.ClinicId,
                _db.Clinics.Where(x => x.Id == w.ClinicId).Select(x => x.Name).FirstOrDefault() ?? "",
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
