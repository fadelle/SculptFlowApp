using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Campaigns;

namespace PlasticSurgery.Persistence.Repositories.Campaigns;

public class CampaignRepository : ICampaignRepository
{
    private readonly ApplicationDbContext _db;

    public CampaignRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public void Add(Campaign campaign) => _db.Campaigns.Add(campaign);

    public void AddRecipient(CampaignRecipient recipient) => _db.CampaignRecipients.Add(recipient);

    public Task<Campaign?> GetAsync(Guid clinicId, Guid campaignId, CancellationToken ct = default) =>
        _db.Campaigns.FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.Id == campaignId, ct);

    public Task<Campaign?> GetWithTemplateAsync(Guid clinicId, Guid campaignId, CancellationToken ct = default) =>
        _db.Campaigns.Include(c => c.WhatsAppTemplate).FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.Id == campaignId, ct);

    public Task<Campaign?> GetByIdAsync(Guid campaignId, CancellationToken ct = default) =>
        _db.Campaigns.FirstOrDefaultAsync(c => c.Id == campaignId, ct);

    public async Task<IReadOnlyList<Campaign>> ListWithTemplateAsync(Guid clinicId, CancellationToken ct = default) =>
        await _db.Campaigns
            .Include(c => c.WhatsAppTemplate)
            .Where(c => c.ClinicId == clinicId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

    public Task<List<CampaignRecipient>> ListRecipientsAsync(IReadOnlyCollection<Guid> campaignIds, CancellationToken ct = default) =>
        _db.CampaignRecipients.Where(r => campaignIds.Contains(r.CampaignId)).ToListAsync(ct);

    public Task<List<CampaignRecipient>> ListRecipientsWithLeadAsync(Guid campaignId, CancellationToken ct = default) =>
        _db.CampaignRecipients
            .Include(r => r.Lead)
            .Where(r => r.CampaignId == campaignId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CampaignRecipient>> ListRecipientsInStatusAsync(Guid campaignId,
        IReadOnlyCollection<string> statuses, CancellationToken ct = default) =>
        await _db.CampaignRecipients
            .Where(r => r.CampaignId == campaignId && statuses.Contains(r.Status))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CampaignRecipient>> ListQueuedBatchAsync(Guid campaignId, int batchSize, CancellationToken ct = default) =>
        await _db.CampaignRecipients
            .Where(r => r.CampaignId == campaignId && r.Status == CampaignRecipientStatus.Queued)
            .OrderBy(r => r.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);

    public Task<int> CountRecipientsInStatusAsync(Guid campaignId, string status, CancellationToken ct = default) =>
        _db.CampaignRecipients.CountAsync(r => r.CampaignId == campaignId && r.Status == status, ct);

    public Task<CampaignRecipient?> GetRecipientAsync(Guid recipientId, CancellationToken ct = default) =>
        _db.CampaignRecipients.FirstOrDefaultAsync(r => r.Id == recipientId, ct);

    public Task<CampaignRecipient?> FindLatestAwaitingReplyAsync(Guid clinicId, Guid leadId, CancellationToken ct = default) =>
        _db.CampaignRecipients
            .Where(r => r.ClinicId == clinicId && r.LeadId == leadId && r.SentAt != null && r.RepliedAt == null)
            .OrderByDescending(r => r.SentAt)
            .Include(r => r.Campaign)
            .FirstOrDefaultAsync(ct);
}
