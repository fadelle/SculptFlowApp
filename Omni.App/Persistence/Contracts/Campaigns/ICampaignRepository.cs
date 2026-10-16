using Omni.App.Entities.Dtos.Campaigns;
using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Campaigns;

/// <summary>Campaigns and their recipients. Returned entities are tracked unless the method says otherwise.</summary>
public interface ICampaignRepository
{
    void Add(Campaign campaign);

    void AddRecipient(CampaignRecipient recipient);

    Task<Campaign?> GetAsync(Guid clinicId, Guid campaignId, CancellationToken ct = default);

    Task<Campaign?> GetWithTemplateAsync(Guid clinicId, Guid campaignId, CancellationToken ct = default);

    Task<Campaign?> GetByIdAsync(Guid campaignId, CancellationToken ct = default);

    /// <summary>The campaign's status as stored right now (read fresh, not from tracked entities), or null if it doesn't
    /// exist. The sender checks it before every message so Stop and Cancel take effect mid-batch.</summary>
    Task<string?> GetStatusReadOnlyAsync(Guid campaignId, CancellationToken ct = default);

    /// <summary>Set-based: moves the campaign to <paramref name="toStatus"/> only if it is still in one of
    /// <paramref name="fromStatuses"/>. Returns false (nothing changed) when it isn't, e.g. it finished meanwhile.</summary>
    Task<bool> TryChangeStatusAsync(Guid clinicId, Guid campaignId, IReadOnlyCollection<string> fromStatuses, string toStatus,
        DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Across all active clinics: scheduled campaigns whose ScheduledAt has passed, then running ones, oldest
    /// first. Read-only.</summary>
    Task<IReadOnlyList<DueCampaign>> ListDueForSendingReadOnlyAsync(DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Newest first, with the template loaded.</summary>
    Task<IReadOnlyList<Campaign>> ListWithTemplateAsync(Guid clinicId, CancellationToken ct = default);

    Task<List<CampaignRecipient>> ListRecipientsAsync(IReadOnlyCollection<Guid> campaignIds, CancellationToken ct = default);

    /// <summary>Oldest first, with the lead loaded.</summary>
    Task<List<CampaignRecipient>> ListRecipientsWithLeadAsync(Guid campaignId, CancellationToken ct = default);

    Task<IReadOnlyList<CampaignRecipient>> ListRecipientsInStatusAsync(Guid campaignId, IReadOnlyCollection<string> statuses,
        CancellationToken ct = default);

    /// <summary>The next queued recipients to send, oldest first.</summary>
    Task<IReadOnlyList<CampaignRecipient>> ListQueuedBatchAsync(Guid campaignId, int batchSize, CancellationToken ct = default);

    Task<int> CountRecipientsInStatusAsync(Guid campaignId, string status, CancellationToken ct = default);

    Task<CampaignRecipient?> GetRecipientAsync(Guid recipientId, CancellationToken ct = default);

    /// <summary>Takes a Postgres session advisory lock on the campaign so only one sender works on it at a time (the
    /// dashboard's Send button and the background sender, or two app instances). Null = someone else is sending it right
    /// now. Dispose the handle to release the lock; it is also released if the connection closes.</summary>
    Task<IAsyncDisposable?> TryLockForSendingAsync(Guid campaignId, CancellationToken ct = default);

    /// <summary>The lead's most recent campaign send still waiting for a reply (RepliedAt null), with its campaign.</summary>
    Task<CampaignRecipient?> FindLatestAwaitingReplyAsync(Guid clinicId, Guid leadId, CancellationToken ct = default);

    /// <summary>The lead's most recent campaign send that was replied to at or after <paramref name="repliedSince"/> and has not
    /// been credited with a booking yet.</summary>
    Task<CampaignRecipient?> FindLatestRepliedAwaitingBookingAsync(Guid clinicId, Guid leadId, DateTimeOffset repliedSince, CancellationToken ct = default);
}
