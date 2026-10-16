using Microsoft.EntityFrameworkCore;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.Campaigns;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Campaigns;

namespace Omni.App.Persistence.Repositories.Campaigns;

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

    public Task<string?> GetStatusReadOnlyAsync(Guid campaignId, CancellationToken ct = default) =>
        _db.Campaigns.AsNoTracking().Where(c => c.Id == campaignId).Select(c => (string?)c.Status).FirstOrDefaultAsync(ct);

    public async Task<bool> TryChangeStatusAsync(Guid clinicId, Guid campaignId, IReadOnlyCollection<string> fromStatuses, string toStatus,
        DateTimeOffset now, CancellationToken ct = default) =>
        await _db.Campaigns
            .Where(c => c.ClinicId == clinicId && c.Id == campaignId && fromStatuses.Contains(c.Status))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, toStatus).SetProperty(c => c.UpdatedAt, now), ct) > 0;

    public async Task<IReadOnlyList<DueCampaign>> ListDueForSendingReadOnlyAsync(DateTimeOffset now, CancellationToken ct = default) =>
        await _db.Campaigns.AsNoTracking()
            .Where(c => c.Clinic!.IsActive
                        && ((c.Status == CampaignStatus.Scheduled && c.ScheduledAt != null && c.ScheduledAt <= now)
                            || c.Status == CampaignStatus.Running))
            .OrderBy(c => c.Status == CampaignStatus.Scheduled ? 0 : 1)
            .ThenBy(c => c.ScheduledAt ?? c.StartedAt ?? c.CreatedAt)
            .Select(c => new DueCampaign(c.Id, c.ClinicId, c.Status))
            .ToListAsync(ct);

    public async Task<IAsyncDisposable?> TryLockForSendingAsync(Guid campaignId, CancellationToken ct = default)
    {
        // A session-level lock lives on one connection, so keep this context's connection open until it is released.
        await _db.Database.OpenConnectionAsync(ct);
        var key = SendingLockKey(campaignId);
        var acquired = await _db.Database
            .SqlQuery<bool>($"select pg_try_advisory_lock({key}) as \"Value\"")
            .SingleAsync(ct);
        if (acquired) return new SendingLock(_db, key);

        await _db.Database.CloseConnectionAsync();
        return null;
    }

    /// <summary>A stable 64-bit lock key per campaign (the first 8 bytes of its random id); a clash with another lock key,
    /// such as the int-sized per-clinic booking lock, is vanishingly unlikely and would only make one wait a cycle.</summary>
    private static long SendingLockKey(Guid campaignId) => BitConverter.ToInt64(campaignId.ToByteArray(), 0);

    private sealed class SendingLock : IAsyncDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly long _key;
        private bool _released;

        public SendingLock(ApplicationDbContext db, long key)
        {
            _db = db;
            _key = key;
        }

        public async ValueTask DisposeAsync()
        {
            if (_released) return;
            _released = true;
            try
            {
                await _db.Database.SqlQuery<bool>($"select pg_advisory_unlock({_key}) as \"Value\"").SingleAsync();
            }
            finally
            {
                // Closing returns the connection to the pool, which resets the session (and with it any lock still held).
                await _db.Database.CloseConnectionAsync();
            }
        }
    }

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

    public Task<CampaignRecipient?> FindLatestRepliedAwaitingBookingAsync(Guid clinicId, Guid leadId, DateTimeOffset repliedSince, CancellationToken ct = default) =>
        _db.CampaignRecipients
            .Where(r => r.ClinicId == clinicId && r.LeadId == leadId && r.RepliedAt != null && r.RepliedAt >= repliedSince && r.BookedAt == null)
            .OrderByDescending(r => r.RepliedAt)
            .FirstOrDefaultAsync(ct);
    public Task<CampaignRecipient?> FindLatestAwaitingReplyAsync(Guid clinicId, Guid leadId, CancellationToken ct = default) =>
        _db.CampaignRecipients
            .Where(r => r.ClinicId == clinicId && r.LeadId == leadId && r.SentAt != null && r.RepliedAt == null)
            .OrderByDescending(r => r.SentAt)
            .Include(r => r.Campaign)
            .FirstOrDefaultAsync(ct);
}
