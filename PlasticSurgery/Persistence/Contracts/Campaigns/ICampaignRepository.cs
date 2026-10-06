using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.Campaigns;

/// <summary>Campaigns and their recipients. Returned entities are tracked unless the method says otherwise.</summary>
public interface ICampaignRepository
{
    void Add(Campaign campaign);

    void AddRecipient(CampaignRecipient recipient);

    Task<Campaign?> GetAsync(Guid clinicId, Guid campaignId, CancellationToken ct = default);

    Task<Campaign?> GetWithTemplateAsync(Guid clinicId, Guid campaignId, CancellationToken ct = default);

    Task<Campaign?> GetByIdAsync(Guid campaignId, CancellationToken ct = default);

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

    /// <summary>The lead's most recent campaign send still waiting for a reply (RepliedAt null), with its campaign.</summary>
    Task<CampaignRecipient?> FindLatestAwaitingReplyAsync(Guid clinicId, Guid leadId, CancellationToken ct = default);
}
